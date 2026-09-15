using LibreHardwareMonitor.PawnIo;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.Windows;

/// <summary>
/// The kernel helper needed to read CPU power registers, and the cleanup of its predecessor.
/// <para>
/// No user mode Windows API exposes the energy counters of a CPU (Intel RAPL, AMD SMU); they live
/// in model specific registers that only kernel code can read. Every tool that reports real CPU
/// watts therefore ships a helper driver.
/// </para>
/// <para>
/// Up to version 0.9.4 the sensor library used WinRing0, a driver that grants any caller
/// unrestricted access to model specific registers and physical memory. That makes an installed
/// copy a ready-made privilege escalation tool for malware, which is why Microsoft Defender
/// classifies it as a threat. Since 0.9.6 the library uses PawnIO instead: a signed driver that
/// only executes sandboxed modules, each limited to the few registers it actually needs.
/// </para>
/// <para>
/// PawnIO is installed separately and deliberately not bundled. Without it PowerPlugin simply
/// estimates the CPU from its load, which is what it already does without administrator rights.
/// </para>
/// </summary>
public static class HelperDriver
{
    /// <summary>Where to get the helper driver, shown in the user interface.</summary>
    public const string DownloadUrl = "https://pawnio.eu";

    public static string Name => "PawnIO";

    public static bool IsInstalled
    {
        get
        {
            try
            {
                return PawnIo.IsInstalled;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                                  or TypeInitializationException or InvalidOperationException)
            {
                DiagnosticsLog.Write("Prüfung auf Hilfstreiber", exception);
                return false;
            }
        }
    }

    public static string? InstalledVersion
    {
        get
        {
            try
            {
                return PawnIo.IsInstalled ? PawnIo.Version?.ToString() : null;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                                  or TypeInitializationException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Removes the WinRing0 driver that versions up to 0.9.4 extracted next to the executable.
    /// <para>
    /// Nothing writes this file any more, but an installation that ran the older build still has
    /// it on disk, where it keeps triggering virus scanners and remains usable by anything that
    /// looks for it. Deleting it on startup gets rid of both problems.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> RemoveLegacyDriverFiles()
    {
        var removed = new List<string>();
        string directory = AppPaths.ApplicationDirectory;

        // The old library named the extracted driver after the host process, and used these
        // fixed names in its other code paths.
        string[] candidates =
        [
            "PowerPlugin.sys",
            "WinRing0.sys",
            "WinRing0x64.sys",
            "inpout32.dll",
            "inpoutx64.dll",
        ];

        foreach (string candidate in candidates)
        {
            string path = Path.Combine(directory, candidate);

            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);
                removed.Add(candidate);
                DiagnosticsLog.Write($"Alten Hilfstreiber entfernt: {path}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still locked by a running driver service, or already quarantined by the
                // virus scanner. Either way it is not worth interrupting the start.
                DiagnosticsLog.Write($"Alter Hilfstreiber {candidate} konnte nicht entfernt werden", exception);
            }
        }

        return removed;
    }
}
