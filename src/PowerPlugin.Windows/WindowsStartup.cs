using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.Windows;

/// <summary>
/// Sets up and inspects the autostart.
/// <para>
/// Two mechanisms, because neither covers both needs: the per user Run key needs no administrator
/// rights but always starts the program unelevated, and a scheduled task starts it elevated but
/// has to be registered once with administrator rights.
/// </para>
/// <para>
/// Reading the state back is less obvious than writing it. A Run entry can be present and still
/// never run, either because Windows switched it off in the autostart list or because the
/// executable carries the elevation compatibility flag. Both are checked here so the user
/// interface can report what actually happens instead of what was asked for.
/// </para>
/// </summary>
public static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string AppCompatLayersKeyPath =
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    private const string ValueName = "PowerPlugin";

    /// <summary>Where PowerPlugin keeps what Windows cannot be asked for afterwards.</summary>
    private const string ProgramKeyPath = @"Software\PowerPlugin";

    /// <summary>The executable the logon task was registered for.</summary>
    private const string TaskExecutableValueName = "TaskExecutable";

    private const string RunAsAdminLayer = "RUNASADMIN";

    /// <summary>Gathers everything the state of the autostart depends on.</summary>
    public static AutostartFacts GetFacts() => new(
        HasRunEntry: HasRunEntry(),
        HasScheduledTask: ScheduledTaskAutostart.Exists(),
        DisabledInTaskManager: IsDisabledInTaskManager(),
        RunAsAdminFlagSet: HasRunAsAdminFlag());

    /// <summary>
    /// Applies the requested mode and tears the other mechanism down, so the two can never end up
    /// registered at the same time. Returns false when the change did not take effect, typically
    /// because a consent prompt was declined.
    /// </summary>
    public static bool Apply(AutostartMode mode)
    {
        return mode switch
        {
            // Register the task first and drop the Run entry only once it exists. Doing it the
            // other way round would leave no autostart at all when the consent prompt is declined.
            AutostartMode.Elevated => ScheduledTaskAutostart.Create() && SetRunEntry(false),

            // "&" and not "&&": both steps have to run even if the first one fails, otherwise a
            // task that could not be removed would block the Run entry from being written.
            AutostartMode.Standard => ScheduledTaskAutostart.Delete() & SetRunEntry(true),
            _ => ScheduledTaskAutostart.Delete() & SetRunEntry(false),
        };
    }

    public static bool HasRunEntry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            DiagnosticsLog.Write("Autostart lesen", exception);
            return false;
        }
    }

    /// <summary>
    /// Windows records entries switched off in the autostart list of the task manager here, while
    /// leaving the Run value itself untouched. Without this check the program would report an
    /// active autostart that Windows has long since disabled.
    /// </summary>
    public static bool IsDisabledInTaskManager()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, writable: false);

            if (key?.GetValue(ValueName) is not byte[] { Length: > 0 } state)
            {
                // No entry means Windows has never been told otherwise, i.e. enabled.
                return false;
            }

            // The first byte carries the flags; the lowest bit marks the entry as disabled
            // (0x02 and 0x06 are enabled, 0x03 and 0x07 disabled).
            return (state[0] & 0x01) != 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            DiagnosticsLog.Write("Autostart-Freigabe lesen", exception);
            return false;
        }
    }

    /// <summary>
    /// True when the executable is marked "run as administrator". Such a program is never started
    /// from the Run key - Explorer processes it unelevated and skips the entry without a word.
    /// </summary>
    public static bool HasRunAsAdminFlag()
    {
        string? executable = GetExecutablePath();
        if (executable is null)
        {
            return false;
        }

        return HasRunAsAdminFlag(Registry.CurrentUser, executable)
            || HasRunAsAdminFlag(Registry.LocalMachine, executable);
    }

    private static bool HasRunAsAdminFlag(RegistryKey root, string executable)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(AppCompatLayersKeyPath, writable: false);

            if (key?.GetValue(executable) is not string layers)
            {
                return false;
            }

            return ContainsRunAsAdmin(layers);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            DiagnosticsLog.Write("Kompatibilitätsmerker lesen", exception);
            return false;
        }
    }

    private static bool ContainsRunAsAdmin(string layers) =>
        layers.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(layer => layer.Equals(RunAsAdminLayer, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sets or clears the "run as administrator" compatibility flag for this executable - the same
    /// switch as the checkbox in the compatibility tab of its properties. With it, a manual start
    /// shows the consent prompt and runs elevated. The per user key needs no administrator rights.
    /// <para>
    /// Only the RUNASADMIN layer is touched; any other compatibility layer on the executable, such
    /// as a DPI override, stays as it is. "~" marks layers set by the user and leads the value, the
    /// way the compatibility tab writes it.
    /// </para>
    /// </summary>
    public static bool SetRunAsAdminFlag(bool enabled)
    {
        string? executable = GetExecutablePath();
        return executable is not null && SetRunAsAdminFlag(enabled, executable);
    }

    private static bool SetRunAsAdminFlag(bool enabled, string executable)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AppCompatLayersKeyPath, writable: true);

            List<string> layers = (key.GetValue(executable) as string ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(layer => layer != "~" && !layer.Equals(RunAsAdminLayer, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (enabled)
            {
                layers.Add(RunAsAdminLayer);
            }

            if (layers.Count == 0)
            {
                key.DeleteValue(executable, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(executable, "~ " + string.Join(' ', layers), RegistryValueKind.String);
            }

            DiagnosticsLog.Write(enabled
                ? $"Kompatibilitätsmerker 'als Administrator ausführen' für {executable} gesetzt."
                : $"Kompatibilitätsmerker 'als Administrator ausführen' für {executable} entfernt.");
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Write("Kompatibilitätsmerker schreiben", exception);
            return false;
        }
    }

    /// <summary>
    /// Carries the start setup over once the program has moved, say from a build folder into
    /// C:\Program Files. The compatibility flag, the Run entry and the logon task are all bound to
    /// the path of the executable and would otherwise keep pointing at a file that is gone.
    /// Returns true when anything changed.
    /// </summary>
    public static bool FollowMove(AutostartFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        string? current = GetExecutablePath();
        if (current is null)
        {
            return false;
        }

        bool changed = TakeOverRunAsAdminFlag(current);

        string? runTarget = AutostartStatus.ExecutableOf(ReadValue(Registry.CurrentUser, RunKeyPath, ValueName));
        if (facts.HasRunEntry && AutostartStatus.ShouldFollowMove(runTarget, current, File.Exists))
        {
            DiagnosticsLog.Write($"Autostart-Eintrag zeigte auf {runTarget}, jetzt auf {current}.");
            changed |= SetRunEntry(true);
        }

        // Registering the task needs administrator rights. Unelevated, it waits for the next
        // start - which elevates itself as long as the task exists.
        string? taskTarget = ReadValue(Registry.CurrentUser, ProgramKeyPath, TaskExecutableValueName);
        if (facts.HasScheduledTask && ElevationHelper.IsElevated &&
            AutostartStatus.ShouldFollowMove(taskTarget, current, File.Exists))
        {
            DiagnosticsLog.Write($"Geplante Aufgabe zeigte auf {taskTarget ?? "einen unbekannten Pfad"}, jetzt auf {current}.");
            changed |= ScheduledTaskAutostart.Create();
        }

        return changed;
    }

    /// <summary>
    /// The flag belongs to one path, so a moved program would silently lose "always run as
    /// administrator". A flag on a copy of the executable that no longer exists is moved over.
    /// </summary>
    private static bool TakeOverRunAsAdminFlag(string current)
    {
        string fileName = Path.GetFileName(current);
        string[] vanished;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AppCompatLayersKeyPath, writable: false);
            if (key is null)
            {
                return false;
            }

            vanished = key.GetValueNames()
                .Where(path => !path.Equals(current, StringComparison.OrdinalIgnoreCase) &&
                               Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                               key.GetValue(path) is string layers && ContainsRunAsAdmin(layers) &&
                               !File.Exists(path))
                .ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Write("Kompatibilitätsmerker lesen", exception);
            return false;
        }

        if (vanished.Length == 0)
        {
            return false;
        }

        foreach (string path in vanished)
        {
            SetRunAsAdminFlag(false, path);
        }

        return HasRunAsAdminFlag(Registry.CurrentUser, current) || SetRunAsAdminFlag(true, current);
    }

    /// <summary>Notes which executable the logon task starts; the task itself is not readable without effort.</summary>
    internal static void RecordTaskExecutable(string? executable)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(ProgramKeyPath, writable: true);

            if (executable is null)
            {
                key.DeleteValue(TaskExecutableValueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(TaskExecutableValueName, executable, RegistryValueKind.String);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Write("Pfad der geplanten Aufgabe merken", exception);
        }
    }

    private static string? ReadValue(RegistryKey root, string keyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(keyPath, writable: false);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Write("Registry lesen", exception);
            return null;
        }
    }

    private static bool SetRunEntry(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                string? executable = GetExecutablePath();
                if (executable is null)
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
                ClearTaskManagerVeto();
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Write("Autostart schreiben", exception);
            return false;
        }
    }

    /// <summary>
    /// Drops a previous "switched off" mark so that re-enabling the autostart inside the program
    /// actually takes effect. Without this the new Run entry would inherit the old veto and the
    /// user would tick a box that changes nothing.
    /// </summary>
    private static void ClearTaskManagerVeto()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Write("Autostart-Freigabe zurücksetzen", exception);
        }
    }

    /// <summary>
    /// Path of the running executable. Single file publishing hides the real path behind the
    /// extraction directory, so the process path is used rather than the assembly location.
    /// </summary>
    public static string? GetExecutablePath()
    {
        try
        {
            string path = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DiagnosticsLog.Write("Programmpfad ermitteln", exception);
            return null;
        }
    }
}

/// <summary>Restarts the program with administrator rights so the CPU power sensors become readable.</summary>
public static class ElevationHelper
{
    // TOKEN_INFORMATION_CLASS.TokenElevationType and TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited.
    private const int TokenElevationTypeClass = 18;
    private const int TokenElevationTypeLimited = 3;

    private static readonly Lazy<bool> Elevated = new(LibreHardwareTelemetryProvider.IsProcessElevated);
    private static readonly Lazy<bool> FilteredAdministrator = new(IsFilteredAdministrator);

    /// <summary>Whether this process runs elevated - fixed for its lifetime, so computed once.</summary>
    public static bool IsElevated => Elevated.Value;

    /// <summary>
    /// Whether the consent prompt elevates this same account: true for an administrator, whose
    /// token UAC merely filters. A standard account would be asked for another account's password
    /// and the program would then run as that other user, with its own settings and history.
    /// </summary>
    public static bool CanElevateSameAccount => IsElevated || FilteredAdministrator.Value;

    private static bool IsFilteredAdministrator()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return GetTokenInformation(identity.Token, TokenElevationTypeClass, out int type, sizeof(int), out _)
                && type == TokenElevationTypeLimited;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            DiagnosticsLog.Write("Kontotyp ermitteln", exception);
            return false;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

    /// <summary>
    /// Starts a second instance through the shell with the "runas" verb and reports whether the
    /// user accepted the consent prompt. The caller is responsible for shutting the current
    /// instance down.
    /// </summary>
    public static bool TryRestartElevated(string arguments = "")
    {
        string? executable = WindowsStartup.GetExecutablePath();
        if (executable is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            // ERROR_CANCELLED (1223) means the user declined the consent prompt.
            DiagnosticsLog.Write("Neustart mit Administratorrechten", exception);
            return false;
        }
    }
}
