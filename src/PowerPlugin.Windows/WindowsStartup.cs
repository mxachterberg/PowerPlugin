using System.Diagnostics;
using System.Security;
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

            return layers.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(layer => layer.Equals("RUNASADMIN", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            DiagnosticsLog.Write("Kompatibilitätsmerker lesen", exception);
            return false;
        }
    }

    /// <summary>
    /// Removes the "run as administrator" compatibility flag for this executable. Used when the
    /// user switches to the elevated autostart, where the flag is both unnecessary - the task
    /// provides the elevation - and harmful, because it would keep breaking a manual Run entry.
    /// </summary>
    public static bool RemoveRunAsAdminFlag()
    {
        string? executable = GetExecutablePath();
        if (executable is null)
        {
            return false;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AppCompatLayersKeyPath, writable: true);

            if (key?.GetValue(executable) is not string layers)
            {
                return true;
            }

            string[] remaining = layers
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(layer => !layer.Equals("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // "~" on its own is the marker without any layer and would be a leftover.
            if (remaining.Length == 0 || remaining.All(layer => layer == "~"))
            {
                key.DeleteValue(executable, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(executable, string.Join(' ', remaining), RegistryValueKind.String);
            }

            DiagnosticsLog.Write($"Kompatibilitätsmerker 'als Administrator ausführen' für {executable} entfernt.");
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Write("Kompatibilitätsmerker entfernen", exception);
            return false;
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
    public static bool IsElevated => LibreHardwareTelemetryProvider.IsProcessElevated();

    /// <summary>
    /// Starts a second instance through the shell with the "runas" verb and reports whether the
    /// user accepted the consent prompt. The caller is responsible for shutting the current
    /// instance down.
    /// </summary>
    public static bool TryRestartElevated()
    {
        string? executable = WindowsStartup.GetExecutablePath();
        if (executable is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
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
