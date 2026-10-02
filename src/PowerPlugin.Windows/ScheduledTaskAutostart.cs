using System.Diagnostics;
using System.Security;
using System.Text;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.Windows;

/// <summary>
/// Autostart through a logon task that runs with the highest available privileges.
/// <para>
/// This is the only way to have autostart and the CPU power sensors at the same time. Explorer
/// works through the Run key unelevated and silently skips anything that would need elevation, so
/// an executable marked "run as administrator" never starts that way. The task scheduler has no
/// such limitation: it launches the program elevated itself, without a prompt at logon.
/// </para>
/// <para>
/// Driven through <c>schtasks.exe</c> with a task definition rather than the COM API, which keeps
/// the program free of another dependency and makes the resulting task inspectable by hand.
/// </para>
/// </summary>
public static class ScheduledTaskAutostart
{
    public const string TaskName = "PowerPlugin";

    /// <summary>Why the last create or delete failed, in words for the user; null after success.</summary>
    public static string? LastError { get; private set; }

    /// <summary>True when the logon task is registered.</summary>
    public static bool Exists()
    {
        // Querying does not need elevation, so this runs without a prompt on every start.
        (int exitCode, _) = RunSchtasks(["/Query", "/TN", TaskName], elevateIfNeeded: false);
        return exitCode == 0;
    }

    /// <summary>
    /// Registers the logon task, replacing an existing one. Needs administrator rights; when the
    /// program runs unelevated this triggers a single consent prompt.
    /// </summary>
    public static bool Create()
    {
        string? executable = WindowsStartup.GetExecutablePath();
        if (executable is null)
        {
            DiagnosticsLog.Write("Geplante Aufgabe: Programmpfad nicht ermittelbar.");
            return false;
        }

        string definitionPath = Path.Combine(Path.GetTempPath(), $"powerplugin-task-{Guid.NewGuid():N}.xml");

        try
        {
            // schtasks insists on UTF-16 for the definition file.
            File.WriteAllText(definitionPath, BuildDefinition(executable), Encoding.Unicode);

            (int exitCode, string output) = RunSchtasks(
                ["/Create", "/TN", TaskName, "/XML", definitionPath, "/F"], elevateIfNeeded: true);

            if (exitCode != 0)
            {
                LastError = Explain(exitCode, output);
                DiagnosticsLog.Write($"Geplante Aufgabe konnte nicht angelegt werden (Code {exitCode}). {output}");
                return false;
            }

            LastError = null;
            WindowsStartup.RecordTaskExecutable(executable);
            DiagnosticsLog.Write($"Geplante Aufgabe '{TaskName}' angelegt für {executable}.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            LastError = exception.Message;
            DiagnosticsLog.Write("Geplante Aufgabe anlegen", exception);
            return false;
        }
        finally
        {
            TryDelete(definitionPath);
        }
    }

    /// <summary>Removes the logon task. Needs administrator rights.</summary>
    public static bool Delete()
    {
        if (!Exists())
        {
            return true;
        }

        (int exitCode, string output) = RunSchtasks(["/Delete", "/TN", TaskName, "/F"], elevateIfNeeded: true);

        if (exitCode != 0)
        {
            LastError = Explain(exitCode, output);
            DiagnosticsLog.Write($"Geplante Aufgabe konnte nicht entfernt werden (Code {exitCode}). {output}");
            return false;
        }

        LastError = null;
        WindowsStartup.RecordTaskExecutable(null);
        DiagnosticsLog.Write($"Geplante Aufgabe '{TaskName}' entfernt.");
        return true;
    }

    /// <summary>
    /// The task definition, in schema version 1.2 so it registers on every supported Windows.
    /// <para>
    /// Only elements of that schema may appear: the task scheduler validates against the declared
    /// version and rejects the whole definition over a single newer element. An earlier revision
    /// carried two Windows 8 additions (DisallowStartOnRemoteAppSession,
    /// UseUnifiedSchedulingEngine) and failed to register for exactly that reason.
    /// </para>
    /// <para>
    /// Three settings matter beyond the obvious ones and are wrong by default for a program that
    /// is meant to run all day: the execution time limit would terminate it after three days, and
    /// the battery settings would stop a power monitor from running on battery - of all things.
    /// </para>
    /// </summary>
    private static string BuildDefinition(string executablePath)
    {
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        string command = SecurityElement.Escape(executablePath);
        string workingDirectory = SecurityElement.Escape(
            Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Startet PowerPlugin bei der Anmeldung mit Administratorrechten, damit die Leistungssensoren der CPU verfügbar sind.</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>
    /// Runs schtasks and returns its exit code and output.
    /// <para>
    /// Creating and deleting a task that runs elevated needs administrator rights. When the program
    /// itself is unelevated, the call has to go through the shell with the runas verb - and a shell
    /// execute cannot redirect output. So in that case an elevated command prompt runs schtasks and
    /// writes its output to a temporary file, which is read back afterwards. Without that, a
    /// rejected task definition would be indistinguishable from a declined consent prompt.
    /// </para>
    /// </summary>
    private static (int ExitCode, string Output) RunSchtasks(string[] arguments, bool elevateIfNeeded)
    {
        bool needsElevation = elevateIfNeeded && !ElevationHelper.IsElevated;
        return needsElevation ? RunElevated(arguments) : RunDirect(arguments);
    }

    private static (int ExitCode, string Output) RunDirect(string[] arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, "schtasks.exe konnte nicht gestartet werden.");
            }

            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output.Trim());
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DiagnosticsLog.Write("schtasks.exe", exception);
            return (-1, exception.Message);
        }
    }

    private static (int ExitCode, string Output) RunElevated(string[] arguments)
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"powerplugin-schtasks-{Guid.NewGuid():N}.txt");

        // With /s, cmd strips exactly the outer pair of quotes and keeps the inner ones intact.
        string command = "schtasks.exe " + string.Join(' ', arguments.Select(Quote)) +
                         $" > {Quote(outputPath)} 2>&1";

        var startInfo = new ProcessStartInfo("cmd.exe", $"/s /c \"{command}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, "schtasks.exe konnte nicht gestartet werden.");
            }

            process.WaitForExit();
            string output = File.Exists(outputPath) ? File.ReadAllText(outputPath) : string.Empty;
            return (process.ExitCode, output.Trim());
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            // ERROR_CANCELLED (1223) means the user declined the consent prompt.
            DiagnosticsLog.Write("schtasks.exe (erhöht)", exception);
            return (exception.NativeErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DiagnosticsLog.Write("schtasks.exe (erhöht)", exception);
            return (-1, exception.Message);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c is '&' or '|' or '<' or '>' or '^' or '(' or ')')
            ? argument
            : $"\"{argument}\"";

    private static string Explain(int exitCode, string output) => exitCode switch
    {
        1223 => "Die Rückfrage der Benutzerkontensteuerung wurde abgelehnt.",
        _ when output.Length > 0 => $"Die Aufgabenplanung meldet: {output}",
        _ => $"Die Aufgabenplanung hat mit Code {exitCode} abgebrochen, ohne Meldung.",
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticsLog.Write($"Temporäre Aufgabendefinition {path} blieb liegen", exception);
        }
    }
}
