using System.Windows;
using System.Windows.Threading;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.App;

/// <summary>
/// Entry point. Guarantees a single running instance, because two instances would fight over
/// the database and show two icons in the notification area.
/// </summary>
internal static class Program
{
    private const string InstanceMutexName = @"Local\PowerPlugin.SingleInstance";
    private const string ShowWindowEventName = @"Local\PowerPlugin.ShowWindow";

    [STAThread]
    public static int Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Ask the instance that is already running to bring its window up, then quit.
            if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out EventWaitHandle? existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }

            return 0;
        }

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
                controller.Start();

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
