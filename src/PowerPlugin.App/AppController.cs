using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PowerPlugin.App.Tray;
using PowerPlugin.App.Ui;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Estimation;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Monitoring;
using PowerPlugin.Core.Statistics;
using PowerPlugin.Core.Storage;
using PowerPlugin.Windows;

namespace PowerPlugin.App;

/// <summary>
/// Wires the sensor stack, the storage and the user interface together and owns their lifetime.
/// </summary>
internal sealed class AppController : IDisposable
{
    /// <summary>How often the statistics are recomputed from the database.</summary>
    private static readonly TimeSpan StatisticsInterval = TimeSpan.FromSeconds(5);

    private readonly SettingsStore _settingsStore = new();
    private readonly SqliteEnergyStore _store;
    private readonly EnergyRecorder _recorder;
    private readonly StatisticsCalculator _calculator;
    private readonly PowerMonitor _monitor;
    private readonly TrayController _tray;
    private readonly MainWindow _window;
    private readonly DispatcherTimer _statisticsTimer;
    private readonly DispatcherTimer _trayTimer;
    private readonly Dispatcher _dispatcher;

    private readonly bool _isFirstRun;

    private AppSettings _settings;
    private EnergyStatistics _statistics = EnergyStatistics.Empty;
    private bool _statisticsRefreshRunning;
    private bool _disposed;

    public AppController()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _isFirstRun = !File.Exists(_settingsStore.FilePath);
        _settings = _settingsStore.Load();

        _store = new SqliteEnergyStore(AppPaths.DatabaseFile);
        _store.Initialize();
        PurgeOldHistory();

        _recorder = new EnergyRecorder(_store, _settings.SampleInterval);
        _calculator = new StatisticsCalculator(_store);

        _monitor = new PowerMonitor(
            new LibreHardwareTelemetryProvider(),
            new ComponentPowerEstimator(_settings.Model),
            _recorder,
            _settings.SampleInterval);

        _tray = new TrayController(_settings);
        _window = new MainWindow(_settings);

        _statisticsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = StatisticsInterval };

        // The tray icon runs on its own, faster clock: it is refreshed at a fixed rate and shows a
        // short term mean, which decouples it from the sampling interval of the sensors.
        _trayTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = _settings.TrayRefreshInterval };

        WireEvents();
    }

    public void Start()
    {
        SyncAutostartState();

        _monitor.Start();
        _window.SetHardwareSummary(_monitor.Inventory);
        _window.SetElevationState(ElevationHelper.IsElevated, _monitor.RequiresElevation);

        _statisticsTimer.Start();
        _trayTimer.Start();
        RefreshStatistics();

        if (_isFirstRun || !_settings.StartMinimized)
        {
            ShowWindow();
        }
        else
        {
            _tray.ShowMessage(
                "PowerPlugin misst mit",
                "Der aktuelle Verbrauch steht im Infobereich. Ein Klick auf das Symbol öffnet die Statistik.");
        }

        DiagnosticsLog.Write("PowerPlugin gestartet.");
    }

    public void ShowWindow()
    {
        _window.ShowAndActivate();
        _window.UpdateLive(_monitor.Current, BuildLiveSeries());
        _window.UpdateStatistics(_statistics);
        _window.SetElevationState(ElevationHelper.IsElevated, _monitor.RequiresElevation);
        RefreshStatistics();
    }

    private void WireEvents()
    {
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _monitor.SamplingFailed += (_, message) =>
            _dispatcher.BeginInvoke(() => DiagnosticsLog.Write($"Messfehler an die Oberfläche gemeldet: {message}"));

        _statisticsTimer.Tick += (_, _) => RefreshStatistics();
        _trayTimer.Tick += (_, _) => RefreshTray();

        _tray.OpenRequested += (_, _) => _dispatcher.BeginInvoke(ShowWindow);
        _tray.ExitRequested += (_, _) => _dispatcher.BeginInvoke(Shutdown);
        _tray.AutostartToggled += (_, enabled) => _dispatcher.BeginInvoke(() => SetAutostart(enabled));

        _window.SettingsChanged += (_, updated) => ApplySettings(updated);
        _window.ExitRequested += (_, _) => Shutdown();
        _window.ResetHistoryRequested += (_, _) => ResetHistory();
        _window.OpenDataFolderRequested += (_, _) => OpenDataFolder();
        _window.RestartElevatedRequested += (_, _) => RestartElevated();

        // Standby would otherwise be integrated as if the machine had kept running.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    private void OnSnapshotUpdated(object? sender, SnapshotEventArgs e)
    {
        // The monitor samples on a background thread while the window is thread bound. The tray
        // icon is not refreshed here - it runs on its own timer with a smoothed value.
        _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (_disposed)
            {
                return;
            }

            if (_window.IsVisible)
            {
                _window.UpdateLive(e.Snapshot, BuildLiveSeries());
            }
        });
    }

    private IReadOnlyList<double> BuildLiveSeries() => _monitor.LiveWattSeries();

    /// <summary>
    /// Redraws the notification area icon. Whether that shows the latest sample or a mean over a
    /// window is the user's choice; an empty window makes the averaging collapse to the latest
    /// sample, so both modes take the same path.
    /// </summary>
    private void RefreshTray()
    {
        if (_disposed)
        {
            return;
        }

        _tray.Update(
            _monitor.Current,
            _statistics,
            _monitor.AverageWattsOver(_settings.EffectiveTrayAverageWindow));
    }

    private void RefreshStatistics()
    {
        if (_disposed || _statisticsRefreshRunning)
        {
            return;
        }

        _statisticsRefreshRunning = true;

        // Persist the partially filled minute first so the figures include the current session.
        _monitor.FlushToDisk();

        decimal price = _settings.PricePerKilowattHour;

        Task.Run(() =>
        {
            try
            {
                return _calculator.Calculate(DateTimeOffset.Now, price);
            }
            catch (Exception exception)
            {
                DiagnosticsLog.Write("Statistik konnte nicht berechnet werden", exception);
                return _statistics;
            }
        }).ContinueWith(task =>
        {
            _dispatcher.BeginInvoke(() =>
            {
                _statisticsRefreshRunning = false;

                if (_disposed)
                {
                    return;
                }

                _statistics = task.Result;
                RefreshTray();

                // While the window sits hidden in the tray, rebuilding its charts and tables
                // every few seconds would be wasted work - it is refreshed when it reappears.
                if (_window.IsVisible)
                {
                    _window.UpdateStatistics(_statistics);
                    _window.SetElevationState(ElevationHelper.IsElevated, _monitor.RequiresElevation);
                }
            });
        }, TaskScheduler.Default);
    }

    private void ApplySettings(AppSettings updated)
    {
        _settings = updated;
        _settingsStore.Save(updated);

        _monitor.SampleInterval = updated.SampleInterval;
        _monitor.UpdateEstimator(new ComponentPowerEstimator(updated.Model));
        _recorder.SampleInterval = updated.SampleInterval;
        _trayTimer.Interval = updated.TrayRefreshInterval;

        _tray.ApplySettings(updated);
        _window.ApplySettings(updated);

        SetAutostart(updated.StartWithWindows);
        PurgeOldHistory();
        RefreshStatistics();

        DiagnosticsLog.Write("Einstellungen übernommen.");
    }

    private void SyncAutostartState()
    {
        bool actual = WindowsStartup.IsEnabled();

        if (actual != _settings.StartWithWindows)
        {
            // The registry is the source of truth: the user may have removed the entry
            // through the Task Manager's autostart tab.
            _settings.StartWithWindows = actual;
            _settingsStore.Save(_settings);
            _window.ApplySettings(_settings);
        }

        _tray.SetAutostartState(actual);
    }

    private void SetAutostart(bool enabled)
    {
        if (WindowsStartup.IsEnabled() == enabled)
        {
            _tray.SetAutostartState(enabled);
            return;
        }

        if (!WindowsStartup.SetEnabled(enabled))
        {
            MessageBox.Show(
                "Der Autostart-Eintrag konnte nicht geändert werden.",
                "PowerPlugin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _settings.StartWithWindows = WindowsStartup.IsEnabled();
        _settingsStore.Save(_settings);
        _tray.SetAutostartState(_settings.StartWithWindows);
    }

    private void ResetHistory()
    {
        MessageBoxResult answer = MessageBox.Show(
            "Alle aufgezeichneten Messwerte werden unwiderruflich gelöscht. Fortfahren?",
            "Verlauf löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _store.Clear();
        _recorder.ResetTiming();
        DiagnosticsLog.Write("Verlauf auf Wunsch des Benutzers gelöscht.");
        RefreshStatistics();
    }

    private void PurgeOldHistory()
    {
        if (_settings.HistoryRetentionDays <= 0)
        {
            return;
        }

        try
        {
            int removed = _store.PurgeOlderThan(
                DateTimeOffset.UtcNow.AddDays(-_settings.HistoryRetentionDays));

            if (removed > 0)
            {
                DiagnosticsLog.Write($"{removed} veraltete Messwerte entfernt.");
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Aufräumen des Verlaufs", exception);
        }
    }

    private static void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DiagnosticsLog.Write("Datenordner öffnen", exception);
        }
    }

    private void RestartElevated()
    {
        if (ElevationHelper.IsElevated)
        {
            return;
        }

        // Buffered energy has to reach the database before the second instance opens it.
        _monitor.FlushToDisk();

        if (ElevationHelper.TryRestartElevated())
        {
            Shutdown();
        }
        else
        {
            MessageBox.Show(
                "Der Neustart mit Administratorrechten wurde abgebrochen.",
                "PowerPlugin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _monitor.FlushToDisk();
                break;

            case PowerModes.Resume:
                _monitor.NotifyResumed();
                break;
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        DiagnosticsLog.Write($"Windows-Sitzung endet ({e.Reason}) - Messwerte werden gesichert.");
        _monitor.FlushToDisk();
    }

    public void Shutdown()
    {
        Dispose();
        Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;

        _statisticsTimer.Stop();
        _trayTimer.Stop();
        _monitor.Dispose();
        _tray.Dispose();
        _store.Dispose();

        DiagnosticsLog.Write("PowerPlugin beendet.");
    }
}
