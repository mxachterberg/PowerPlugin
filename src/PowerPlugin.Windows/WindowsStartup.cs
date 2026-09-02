using System.Diagnostics;
using Microsoft.Win32;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.Windows;

/// <summary>
/// Registers the program in the per user autostart key. No administrator rights are needed,
/// which is why HKEY_CURRENT_USER is used instead of the machine wide key.
/// </summary>
public static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PowerPlugin";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            DiagnosticsLog.Write("Autostart lesen", exception);
            return false;
        }
    }

    /// <summary>Enables or disables the autostart entry. Returns true when the change was applied.</summary>
    public static bool SetEnabled(bool enabled)
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
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            DiagnosticsLog.Write("Autostart schreiben", exception);
            return false;
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
    /// user accepted the UAC prompt. The caller is responsible for shutting the current instance down.
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
            // ERROR_CANCELLED (1223) means the user declined the UAC prompt.
            DiagnosticsLog.Write("Neustart mit Administratorrechten", exception);
            return false;
        }
    }
}
