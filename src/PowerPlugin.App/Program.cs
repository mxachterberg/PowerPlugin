using System.Windows;
using System.Windows.Threading;
using PowerPlugin.Core.Monitoring;
using PowerPlugin.Windows;

namespace PowerPlugin.App;

/// <summary>
/// Entry point. Guarantees a single running instance, because two instances would fight over
/// the database and show two icons in the notification area.
/// </summary>
internal static class Program
{
    private const string InstanceMutexName = @"Local\PowerPlugin.SingleInstance";
    private const string ShowWindowEventName = @"Local\PowerPlugin.ShowWindow";

    /// <summary>
    /// Passed by an instance that starts its successor and then quits. The successor waits for it
    /// to let go of the single instance mutex and never elevates itself a second time.
    /// </summary>
    public const string RestartedArgument = "--restarted";

    /// <summary>Opens the window right away instead of starting in the notification area.</summary>
    public const string ShowWindowArgument = "--show";

    /// <summary>How long a restarted instance waits for its predecessor to shut down.</summary>
    private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(20);

    [STAThread]
    public static int Main(string[] args)
    {
        bool restarted = args.Contains(RestartedArgument, StringComparer.OrdinalIgnoreCase);
        bool showWindow = args.Contains(ShowWindowArgument, StringComparer.OrdinalIgnoreCase);

        Mutex? mutex = AcquireInstance(restarted);
        if (mutex is null)
        {
            return 0;
        }

        using (mutex)
        {
            try
            {
                // The elevated successor waits for the mutex, which is released on the way out.
                if (!restarted && ShouldRunElevated() && RestartElevated(showWindow))
                {
                    return 0;
                }

                return Run(showWindow);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }

    /// <summary>
    /// Takes the single instance mutex. When another instance holds it, that one is asked to show
    /// its window and null is returned - except for a restart, which waits for its predecessor.
    /// </summary>
    private static Mutex? AcquireInstance(bool restarted)
    {
        Mutex mutex;
        bool createdNew;

        try
        {
            mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out createdNew);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // The mutex of an elevated instance is off limits to an unelevated process.
            ReportElevatedInstance();
            return null;
        }

        if (createdNew)
        {
            return mutex;
        }

        // Handing the request to show the window to an instance that is about to quit would leave
        // no instance at all, so a restart waits until the previous one is gone.
        if (restarted && WaitForPredecessor(mutex))
        {
            return mutex;
        }

        SignalRunningInstance();
        mutex.Dispose();
        return null;
    }

    private static bool WaitForPredecessor(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(HandoverTimeout);
        }
        catch (AbandonedMutexException)
        {
            // The predecessor ended without releasing it; the mutex belongs to this thread anyway.
            return true;
        }
    }

    /// <summary>Asks the instance that is already running to bring its window up.</summary>
    private static void SignalRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out EventWaitHandle? existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            ReportElevatedInstance();
        }
    }

    /// <summary>
    /// An unelevated process can neither open the objects of an elevated one nor send messages
    /// to its windows, so the running instance cannot be asked to show itself - only pointed to.
    /// </summary>
    private static void ReportElevatedInstance()
    {
        DiagnosticsLog.Write("Zweiter Start ohne Administratorrechte, während PowerPlugin mit Administratorrechten läuft.");

        MessageBox.Show(
            "PowerPlugin läuft bereits mit Administratorrechten.\n\n" +
            "Das Fenster öffnest du über das Symbol im Infobereich der Taskleiste - ein Start " +
            "ohne Administratorrechte kann es von hier aus nicht aufrufen.",
            "PowerPlugin",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// "Always run as administrator" holds for every start, not only for those through Explorer,
    /// which honours the compatibility flag. Started any other way - or by a version that has
    /// just put a lost flag back - the program elevates itself here, once.
    /// </summary>
    private static bool ShouldRunElevated()
    {
        // A standard account would have to type in another account's password, and the program
        // would then run as that account - not what the switch means, so it stays as it is.
        if (ElevationHelper.IsElevated || !ElevationHelper.CanElevateSameAccount)
        {
            return false;
        }

        // The logon task exists only while "always run as administrator" is on, so it counts as
        // that choice too. Queried second: it starts schtasks.exe, the flag is a registry read.
        return WindowsStartup.HasRunAsAdminFlag() || ScheduledTaskAutostart.Exists();
    }

    private static bool RestartElevated(bool showWindow)
    {
        string arguments = showWindow ? $"{RestartedArgument} {ShowWindowArgument}" : RestartedArgument;

        if (ElevationHelper.TryRestartElevated(arguments))
        {
            DiagnosticsLog.Write("Mit Administratorrechten neu gestartet, wie in den Einstellungen festgelegt.");
            return true;
        }

        // Declined: carry on without, the CPU is estimated until the next start.
        DiagnosticsLog.Write("Administratorrechte beim Start abgelehnt - PowerPlugin läuft ohne.");
        return false;
    }

    private static int Run(bool showWindow)
    {
        // The notification area menu is a Windows Forms control, so its renderer has to be
        // initialised before the first one is created.
        System.Windows.Forms.Application.EnableVisualStyles();

        using var showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);

        var application = new Application
        {
            // The program lives in the notification area, so closing the window must not end it.
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        AppController? controller = null;

        application.DispatcherUnhandledException += (_, e) =>
        {
            DiagnosticsLog.Write("Unbehandelter Fehler in der Oberfläche", e.Exception);

            MessageBox.Show(
                $"Ein unerwarteter Fehler ist aufgetreten:\n\n{e.Exception.Message}\n\n" +
                "Das Programm läuft weiter. Details stehen in der Protokolldatei im Datenordner.",
                "PowerPlugin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticsLog.Write("Unbehandelter Fehler", e.ExceptionObject as Exception ?? new Exception("Unbekannt"));

        application.Startup += (_, _) =>
        {
            try
            {
                controller = new AppController();
                controller.Start(showWindow);

                StartShowWindowListener(showWindowEvent, application.Dispatcher, () => controller?.ShowWindow());
            }
            catch (Exception exception)
            {
                DiagnosticsLog.Write("Start fehlgeschlagen", exception);

                MessageBox.Show(
                    $"PowerPlugin konnte nicht gestartet werden:\n\n{exception.Message}",
                    "PowerPlugin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                application.Shutdown(1);
            }
        };

        application.Exit += (_, _) => controller?.Dispose();

        return application.Run();
    }

    /// <summary>
    /// Waits for a second instance to signal that the window should be shown. A background
    /// thread is enough here - the actual work is marshalled onto the UI dispatcher.
    /// </summary>
    private static void StartShowWindowListener(EventWaitHandle handle, Dispatcher dispatcher, Action showWindow)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                    dispatcher.BeginInvoke(showWindow);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (AbandonedMutexException)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "PowerPlugin.ShowWindowListener",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
