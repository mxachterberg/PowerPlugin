using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Monitoring;
using PowerPlugin.Core.Updates;
using PowerPlugin.Windows;

namespace PowerPlugin.App.Updates;

internal enum UpdateStatus
{
    Idle,
    Checking,
    Current,
    Available,
    Downloading,
    Installing,
    Error,

    /// <summary>A development build, which does not replace itself.</summary>
    Unsupported,
}

/// <summary>What the user interface shows about updates.</summary>
internal sealed record UpdateState(
    UpdateStatus Status,
    string CurrentVersion,
    UpdateManifest? Latest,
    double Progress,
    string? Error,
    DateTimeOffset? LastChecked,
    bool CanInstall)
{
    public bool IsBusy => Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Installing;

    /// <summary>The release has a small update that fits exactly the installed version.</summary>
    public bool IsSmall => CanInstall && Latest?.Delta is { } delta && delta.From == CurrentVersion;

    /// <summary>What the update will download: the small update if it fits, otherwise the full package.</summary>
    public long DownloadSize => IsSmall ? Latest!.Delta!.File.Size : Latest?.Package.Size ?? 0;
}

/// <summary>What changed with the update that was just installed, for the "new in" dialog.</summary>
internal sealed record InstalledUpdate(string From, string To, IReadOnlyList<string> Notes, bool Automatic = false);

/// <summary>
/// Updates, built like those of Clavichat: look for a new version shortly after the start (and
/// once a day, as PowerPlugin runs for days in the tray), offer it, download and install it.
/// <para>
/// Source: the releases of the public GitHub repository. The release workflow builds the package,
/// signs the update list and attaches both to the release.
/// </para>
/// <para>
/// Security: the update list is signed with Ed25519 (see <see cref="UpdateManifestReader"/>), with
/// a key of PowerPlugin's own. The list states size and SHA-256 of the package; it is installed
/// only when signature and checksum match and the version is newer. Whoever gets hold of the
/// repository or the download can withhold updates, but not smuggle in a program without the key.
/// </para>
/// <para>
/// Installing: the package is a zip of the published application. It is unpacked to a temporary
/// folder, then a small PowerShell script waits for this process to end, swaps the program folder
/// for the new one - the old one is kept until the new one is in place - and starts PowerPlugin
/// again. In C:\Program Files that needs administrator rights; an elevated PowerPlugin passes its
/// rights on, an unelevated one asks once.
/// </para>
/// </summary>
internal sealed class UpdateService : IDisposable
{
    public const string RepositoryUrl = "https://github.com/mxachterberg/PowerPlugin";

    /// <summary>
    /// The update list of the newest release. GitHub redirects "latest" to the newest published
    /// release, so the list is always the current one without a server of our own.
    /// </summary>
    public const string ManifestUrl = RepositoryUrl + "/releases/latest/download/update.json";

    /// <summary>
    /// The publisher's key (Ed25519, SPKI/DER, Base64), printed by scripts/update-key.js. The
    /// private half is the repository secret UPDATE_SIGNING_KEY, used only by the release workflow.
    /// </summary>
    public const string PublicKey = "MCowBQYDK2VwAyEAlGx6CauvrfaZJAUtLpqsw19KVCrB2hk2vpfBkKCYnwI=";

    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan Stall = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer;
    private UpdateState _state;
    private Task? _check;
    private bool _auto;
    private bool _disposed;

    public UpdateService()
    {
        string version = AppVersion.Of(Assembly.GetEntryAssembly()?.GetName().Version);
        _state = new UpdateState(
            IsSupported ? UpdateStatus.Idle : UpdateStatus.Unsupported, version, null, 0, null, null, IsSupported);

        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerPlugin", version));
        _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = StartDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = CheckInterval;
            _ = CheckAsync(automatic: true);
        };
    }

    /// <summary>Raised on the user interface thread whenever anything about the state changes.</summary>
    public event EventHandler<UpdateState>? StateChanged;

    /// <summary>An automatic check found a new version; the user should be asked once.</summary>
    public event EventHandler<UpdateManifest>? Found;

    public UpdateState State => _state;

    /// <summary>
    /// A version whose installation was just attempted and did not arrive. It is not installed
    /// automatically again in this session - otherwise a broken update would restart PowerPlugin
    /// over and over.
    /// </summary>
    public string? FailedVersion { get; private set; }

    /// <summary>
    /// A build straight out of the source tree replaces nothing - only the published program
    /// does, wherever it was installed.
    /// </summary>
    public static bool IsSupported { get; } = !IsDevelopmentBuild(AppPaths.ApplicationDirectory);

    private static string WorkDirectory => Path.Combine(Path.GetTempPath(), "PowerPlugin-Update");

    private static string PendingFile => Path.Combine(AppPaths.DataDirectory, "update-pending.json");

    private static string LogFile => Path.Combine(AppPaths.DataDirectory, "update.log");

    public void Start(bool automatic)
    {
        RemoveLeftovers();
        SetAutomatic(automatic);
    }

    public void SetAutomatic(bool automatic)
    {
        if (automatic == _auto && (_timer.IsEnabled || !automatic))
        {
            return;
        }

        _auto = automatic;
        _timer.Stop();

        if (automatic && IsSupported)
        {
            _timer.Interval = StartDelay;
            _timer.Start();
        }
    }

    /// <summary>
    /// Looks for a newer version. A development build may look too - it only cannot install.
    /// </summary>
    public Task CheckAsync(bool automatic)
    {
        if (_disposed || _state.Status is UpdateStatus.Downloading or UpdateStatus.Installing)
        {
            return Task.CompletedTask;
        }

        return _check ??= RunCheckAsync(automatic);
    }

    private async Task RunCheckAsync(bool automatic)
    {
        try
        {
            Set(_state with { Status = UpdateStatus.Checking, Error = null });

            UpdateManifest manifest = await FetchManifestAsync();
            bool available = AppVersion.IsNewer(manifest.Version, _state.CurrentVersion);

            Set(_state with
            {
                Status = available ? UpdateStatus.Available : UpdateStatus.Current,
                Latest = available ? manifest : null,
                LastChecked = DateTimeOffset.Now,
            });

            if (available && automatic)
            {
                Found?.Invoke(this, manifest);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            DiagnosticsLog.Write("Suche nach Updates", exception);
            Set(_state with { Status = UpdateStatus.Error, Error = Describe(exception), LastChecked = DateTimeOffset.Now });
        }
        finally
        {
            _check = null;
        }
    }

    /// <summary>
    /// Downloads, checks and unpacks the new version and starts the script that puts it in place.
    /// Returns true when that script runs - the caller then has to end the program, which is what
    /// the script waits for.
    /// </summary>
    /// <param name="automatic">
    /// Installed without being asked: PowerPlugin comes back the way it was, without opening its
    /// window, and announces the update in the notification area instead.
    /// </param>
    public async Task<bool> InstallAsync(bool automatic = false)
    {
        if (!IsSupported || _disposed || _state.Status is UpdateStatus.Downloading or UpdateStatus.Installing)
        {
            return false;
        }

        if (_check is { } running)
        {
            await running;
        }

        try
        {
            UpdateManifest? manifest = _state.Latest;
            if (manifest is null || !AppVersion.IsNewer(manifest.Version, _state.CurrentVersion))
            {
                manifest = await FetchManifestAsync();

                if (!AppVersion.IsNewer(manifest.Version, _state.CurrentVersion))
                {
                    Set(_state with { Status = UpdateStatus.Current, Latest = null, LastChecked = DateTimeOffset.Now });
                    return false;
                }
            }

            Set(_state with { Status = UpdateStatus.Downloading, Latest = manifest, Progress = 0, Error = null });

            string? staging = null;

            // The small update first, like Clavichat's: only the changed files. If anything about
            // it does not fit, the full package takes over.
            if (_state.IsSmall)
            {
                try
                {
                    string delta = await DownloadAsync(manifest.Delta!.File, manifest.Version);
                    Set(_state with { Status = UpdateStatus.Installing, Progress = 1 });
                    staging = await Task.Run(() => ApplyDelta(delta, manifest.Version));
                    DiagnosticsLog.Write($"Kleines Update auf {manifest.Version} vorbereitet.");
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    DiagnosticsLog.Write("Kleines Update - es folgt das volle Paket", exception);
                    Set(_state with { Status = UpdateStatus.Downloading, Progress = 0 });
                }
            }

            if (staging is null)
            {
                string package = await DownloadAsync(manifest.Package, manifest.Version);
                Set(_state with { Status = UpdateStatus.Installing, Progress = 1 });
                staging = await Task.Run(() => Unpack(package, manifest.Version));
            }

            WritePending(new InstalledUpdate(_state.CurrentVersion, manifest.Version, manifest.Notes, automatic));
            StartSwap(staging, manifest.Version, showWindow: !automatic);
            return true;
        }
        catch (Exception exception) when (IsExpected(exception) || exception is System.ComponentModel.Win32Exception)
        {
            DiagnosticsLog.Write("Update installieren", exception);
            TryDelete(PendingFile);
            Set(_state with { Status = UpdateStatus.Error, Error = Describe(exception) });
            return false;
        }
    }

    /// <summary>
    /// After an update: what it brought, once. Null when this start is not the first after an
    /// update - or when the update did not go through and the old version runs again.
    /// </summary>
    public InstalledUpdate? TakeInstalledUpdate()
    {
        try
        {
            if (!File.Exists(PendingFile))
            {
                return null;
            }

            InstalledUpdate? update = JsonSerializer.Deserialize<InstalledUpdate>(File.ReadAllText(PendingFile));
            File.Delete(PendingFile);

            if (update is null || update.To != _state.CurrentVersion)
            {
                FailedVersion = update?.To;
                DiagnosticsLog.Write($"Update auf {update?.To} ist nicht angekommen; es läuft {_state.CurrentVersion}.");
                return null;
            }

            return update;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticsLog.Write("Neuerungen des Updates lesen", exception);
            return null;
        }
    }

    // ---- Steps ------------------------------------------------------------------------

    private async Task<UpdateManifest> FetchManifestAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using HttpResponseMessage response = await _http.GetAsync(
            ManifestUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new UpdateException("Das neueste Release auf GitHub hat noch keine Update-Liste.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateException($"GitHub antwortet mit {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength > UpdateManifestReader.MaxLength)
        {
            throw new UpdateException("Die Update-Liste ist ungültig.");
        }

        string text = await response.Content.ReadAsStringAsync(timeout.Token);
        return UpdateManifestReader.Verify(text, PublicKey);
    }

    /// <summary>Loads the package, checking size and SHA-256 while it arrives.</summary>
    private async Task<string> DownloadAsync(UpdateFile file, string version)
    {
        Directory.CreateDirectory(WorkDirectory);
        string target = Path.Combine(WorkDirectory, file.Name);
        string part = target + ".part";
        // From the release of exactly this version: "latest" could already be a newer one.
        var source = new Uri($"{RepositoryUrl}/releases/download/v{version}/{Uri.EscapeDataString(file.Name)}");

        try
        {
            using var stall = new CancellationTokenSource(Stall);
            using HttpResponseMessage response = await _http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, stall.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"Die neue Fassung ließ sich nicht laden ({(int)response.StatusCode}).");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            int shownPercent = -1;

            await using (Stream body = await response.Content.ReadAsStreamAsync(stall.Token))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                int read;

                while ((read = await body.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(Stall);
                    received += read;

                    if (received > file.Size)
                    {
                        throw new UpdateException("Die Datei ist größer als angekündigt - abgebrochen.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), stall.Token);

                    int percent = (int)(received * 100 / file.Size);
                    if (percent != shownPercent)
                    {
                        shownPercent = percent;
                        Set(_state with { Progress = (double)received / file.Size });
                    }
                }
            }

            if (received != file.Size ||
                !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("Die Prüfsumme der neuen Fassung stimmt nicht - sie wird nicht installiert.");
            }

            File.Move(part, target, overwrite: true);
            return target;
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    private static string Unpack(string package, string version)
    {
        string staging = Path.Combine(WorkDirectory, version);

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            // Refuses entries that would land outside the folder.
            ZipFile.ExtractToDirectory(package, staging);
        }
        finally
        {
            TryDelete(package);
        }

        string executable = Path.GetFileName(WindowsStartup.GetExecutablePath() ?? "PowerPlugin.exe");
        if (!File.Exists(Path.Combine(staging, executable)))
        {
            throw new UpdateException($"Im Paket fehlt {executable} - es wird nicht installiert.");
        }

        return staging;
    }

    /// <summary>
    /// The small update: the installed program, copied, with the changed files of the delta on
    /// top and the files it drops removed. The result goes through the same swap as a full package.
    /// </summary>
    private static string ApplyDelta(string deltaZip, string version)
    {
        string staging = Path.Combine(WorkDirectory, version);
        string installed = AppPaths.ApplicationDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            // The data folder of a portable installation is carried over by the swap itself.
            CopyDirectory(installed, staging, skipTopLevel: "Data");

            DeltaDescription? description = null;

            using (ZipArchive archive = ZipFile.OpenRead(deltaZip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName == "delta.json")
                    {
                        using var reader = new StreamReader(entry.Open());
                        description = JsonSerializer.Deserialize<DeltaDescription>(
                            reader.ReadToEnd(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        continue;
                    }

                    string target = Inside(root, entry.FullName);
                    if (entry.FullName.EndsWith('/'))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }

            if (description is null || description.To != version)
            {
                throw new UpdateException("Das kleine Update passt nicht zu dieser Fassung.");
            }

            foreach (string removed in description.Removed ?? [])
            {
                string target = Inside(root, removed);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
        }
        catch (JsonException exception)
        {
            throw new UpdateException("Das kleine Update ist beschädigt.", exception);
        }
        finally
        {
            TryDelete(deltaZip);
        }

        string executable = Path.GetFileName(WindowsStartup.GetExecutablePath() ?? "PowerPlugin.exe");
        if (!File.Exists(Path.Combine(staging, executable)))
        {
            throw new UpdateException($"Nach dem kleinen Update fehlt {executable}.");
        }

        return staging;
    }

    /// <summary>A path from the delta, refused if it would land outside the folder.</summary>
    private static string Inside(string root, string relative)
    {
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException("Das kleine Update enthält einen ungültigen Pfad.");
        }

        return target;
    }

    private static void CopyDirectory(string source, string target, string? skipTopLevel)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            string name = Path.GetFileName(directory);
            if (!string.Equals(name, skipTopLevel, StringComparison.OrdinalIgnoreCase))
            {
                CopyDirectory(directory, Path.Combine(target, name), skipTopLevel: null);
            }
        }
    }

    /// <summary>The list inside a small update (written by scripts/build-release.ps1).</summary>
    private sealed record DeltaDescription(string From, string To, IReadOnlyList<string>? Removed);

    /// <summary>
    /// Whether installing would bring up a consent prompt - in C:\Program Files, unless PowerPlugin
    /// already runs elevated. An automatic update does not spring that on the user unasked.
    /// </summary>
    public static bool InstallNeedsElevation =>
        !ElevationHelper.IsElevated && !CanReplace(AppPaths.ApplicationDirectory.TrimEnd(Path.DirectorySeparatorChar));

    /// <summary>
    /// The running program holds its files open, so the swap happens after it has ended. The old
    /// folder is renamed rather than deleted first: if the new one cannot be put in place, the
    /// old one comes back. A portable installation keeps its data folder and marker file.
    /// </summary>
    private static void StartSwap(string staging, string version, bool showWindow)
    {
        string executable = WindowsStartup.GetExecutablePath()
            ?? throw new UpdateException("Der Programmpfad ließ sich nicht ermitteln.");
        string directory = Path.GetDirectoryName(executable)!.TrimEnd(Path.DirectorySeparatorChar);
        string name = Path.GetFileName(directory);

        static string Q(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        static string Log(string text) =>
            $"Add-Content -Encoding UTF8 -LiteralPath {Q(LogFile)} -Value ((Get-Date -Format s) + {Q(" " + text)})";

        string script = string.Join("\r\n",
            "$ErrorActionPreference = 'Stop'",
            $"$dir = {Q(directory)}",
            $"$new = {Q(directory + ".new")}",
            $"$old = {Q(directory + ".old")}",
            "try {",
            "  " + Log($"Update auf {version}: Hilfsskript wartet auf Prozess {Environment.ProcessId}"),
            $"  Wait-Process -Id {Environment.ProcessId} -Timeout 120 -ErrorAction SilentlyContinue",
            "  foreach ($leftover in $new, $old) { if (Test-Path -LiteralPath $leftover) { Remove-Item -LiteralPath $leftover -Recurse -Force } }",
            $"  Copy-Item -LiteralPath {Q(staging)} -Destination $new -Recurse -Force",
            "  foreach ($keep in 'Data', 'portable.txt') {",
            "    $item = Join-Path $dir $keep",
            "    if (Test-Path -LiteralPath $item) { Copy-Item -LiteralPath $item -Destination (Join-Path $new $keep) -Recurse -Force }",
            "  }",
            "  $moved = $false",
            "  for ($i = 0; $i -lt 60 -and -not $moved; $i++) {",
            $"    try {{ Rename-Item -LiteralPath $dir -NewName {Q(name + ".old")}; $moved = $true }}",
            "    catch { $reason = $_.Exception.Message; Start-Sleep -Milliseconds 500 }",
            "  }",
            "  if (-not $moved) { throw \"Der Programmordner bleibt gesperrt: $reason\" }",
            $"  try {{ Rename-Item -LiteralPath $new -NewName {Q(name)} }}",
            $"  catch {{ Rename-Item -LiteralPath $old -NewName {Q(name)}; throw }}",
            "  " + Log("Neue Fassung eingesetzt"),
            "  Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue",
            "}",
            "catch {",
            $"  Add-Content -Encoding UTF8 -LiteralPath {Q(LogFile)} -Value ((Get-Date -Format s) + ' Update fehlgeschlagen: ' + $_.Exception.Message)",
            "}",
            "finally {",
            $"  Remove-Item -LiteralPath {Q(staging)} -Recurse -Force -ErrorAction SilentlyContinue",
            $"  Start-Process -FilePath {Q(executable)}{(showWindow ? $" -ArgumentList '{Program.ShowWindowArgument}'" : string.Empty)} -WorkingDirectory $dir",
            "  " + Log("PowerPlugin gestartet"),
            "}");

        string scriptFile = Path.Combine(WorkDirectory, "update.ps1");

        // With a byte order mark, or Windows PowerShell 5 misreads the umlauts.
        File.WriteAllText(scriptFile, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        bool elevate = !ElevationHelper.IsElevated && !CanReplace(directory);
        var startInfo = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptFile}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = WorkDirectory,
            Verb = elevate ? "runas" : string.Empty,
        };

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new UpdateException("Das Update braucht Administratorrechte für den Programmordner - die Rückfrage wurde abgelehnt.", exception);
        }

        DiagnosticsLog.Write($"Update auf {version}: Hilfsskript gestartet{(elevate ? " (mit Administratorrechten)" : string.Empty)}.");
    }

    // ---- Helpers ----------------------------------------------------------------------

    /// <summary>Whether this process may rename the program folder - C:\Program Files needs elevation.</summary>
    private static bool CanReplace(string directory)
    {
        string probe = $"{directory}.probe-{Guid.NewGuid():N}";

        try
        {
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool IsDevelopmentBuild(string directory) =>
        directory.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
        directory.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);

    private static void WritePending(InstalledUpdate update) =>
        File.WriteAllText(PendingFile, JsonSerializer.Serialize(update));

    /// <summary>Clears what an earlier update left behind.</summary>
    private static void RemoveLeftovers()
    {
        try
        {
            if (Directory.Exists(WorkDirectory))
            {
                Directory.Delete(WorkDirectory, recursive: true);
            }

            string directory = AppPaths.ApplicationDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(directory + ".old"))
            {
                Directory.Delete(directory + ".old", recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticsLog.Write("Reste eines Updates entfernen", exception);
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is UpdateException or HttpRequestException or OperationCanceledException
            or IOException or UnauthorizedAccessException or InvalidDataException;

    private static string Describe(Exception exception) => exception switch
    {
        UpdateException => exception.Message,
        HttpRequestException or OperationCanceledException => "Keine Verbindung zu GitHub.",
        UnauthorizedAccessException => "Der Programmordner ist schreibgeschützt - die neue Fassung lässt sich dort nicht ablegen.",
        InvalidDataException => "Das Paket der neuen Fassung ist beschädigt.",
        IOException io when io.HResult == unchecked((int)0x80070070) => "Nicht genug Speicherplatz für die neue Fassung.",
        _ => exception.Message,
    };

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Set(UpdateState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _http.Dispose();
    }
}
