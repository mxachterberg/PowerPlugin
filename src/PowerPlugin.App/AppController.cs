using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PowerPlugin.App.Tray;
using PowerPlugin.App.Ui;
using PowerPlugin.Core.Configuration;
using PowerPlugin.Core.Estimation;
using PowerPlugin.Core.Hardware;
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
    private readonly LibreHardwareTelemetryProvider _provider = new();
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

        // Builds up to 0.9.4 of the sensor library dropped a WinRing0 kernel driver next to the
        // executable. Nothing writes it any more; an existing copy is removed here.
        HelperDriver.RemoveLegacyDriverFiles();

        _store = new SqliteEnergyStore(AppPaths.DatabaseFile);
        _store.Initialize();
        PurgeOldHistory();

        _recorder = new EnergyRecorder(_store, _settings.SampleInterval);
        _calculator = new StatisticsCalculator(_store);

        _monitor = new PowerMonitor(
            _provider,
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

    /// <param name="showWindow">Opens the window even when the program is set to start in the tray.</param>
    public void Start(bool showWindow = false)
    {
        SyncAutostartState();

        _monitor.Start();
        _window.SetHardwareSummary(_monitor.Inventory);
        _window.SetSensorAccess(_monitor.CpuSensorAccess, _monitor.HelperDriverVersion);

        _statisticsTimer.Start();
        _trayTimer.Start();
        RefreshStatistics();

        if (_isFirstRun || showWindow || !_settings.StartMinimized)
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
        _window.SetSensorAccess(_monitor.CpuSensorAccess, _monitor.HelperDriverVersion);
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
        _tray.AutostartToggled += (_, enabled) => _dispatcher.BeginInvoke(
            () => ApplyStartup(_settings.RunAsAdministrator, enabled));

        _window.SettingsChanged += (_, updated) => ApplySettings(updated);
        _window.ExitRequested += (_, _) => Shutdown();
        _window.ResetHistoryRequested += (_, _) => ResetHistory();
        _window.OpenDataFolderRequested += (_, _) => OpenDataFolder();
        _window.RestartElevatedRequested += (_, _) => RestartElevated();
        _window.SensorReportRequested += (_, _) => CreateSensorReport();

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
                    _window.SetSensorAccess(_monitor.CpuSensorAccess, _monitor.HelperDriverVersion);
                }
            });
        }, TaskScheduler.Default);
    }

    private void ApplySettings(AppSettings updated)
    {
        bool wasRunAsAdministrator = _settings.RunAsAdministrator;
        _settings = updated;
        _settingsStore.Save(updated);

        _monitor.SampleInterval = updated.SampleInterval;
        _monitor.UpdateEstimator(new ComponentPowerEstimator(updated.Model));
        _recorder.SampleInterval = updated.SampleInterval;
        _trayTimer.Interval = updated.TrayRefreshInterval;

        _tray.ApplySettings(updated);
        _window.ApplySettings(updated);

        ApplyStartup(updated.RunAsAdministrator, updated.StartWithWindows);
        PurgeOldHistory();
        RefreshStatistics();

        DiagnosticsLog.Write("Einstellungen übernommen.");

        // The switch acts on the next start; offer that start right away. Deferred, because the
        // restart disposes everything this method and the settings page still work with.
        if (!wasRunAsAdministrator && _settings.RunAsAdministrator && !ElevationHelper.IsElevated)
        {
            _dispatcher.BeginInvoke(OfferElevatedRestart);
        }
    }

    private void OfferElevatedRestart()
    {
        if (_disposed)
        {
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            "Ab jetzt fragt Windows bei jedem Start von PowerPlugin nach Administratorrechten.\n\n" +
            "Diese Sitzung läuft noch ohne. Jetzt mit Administratorrechten neu starten?",
            "PowerPlugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            RestartElevated();
        }
    }

    /// <summary>
    /// Takes both switches from the system rather than from the settings file - they can be
    /// changed outside the program, through the compatibility tab of the executable or the
    /// autostart list of the task manager - and repairs what can be left behind: a moved program,
    /// a lost compatibility flag, a Run entry that the flag blocks.
    /// </summary>
    private void SyncAutostartState()
    {
        AutostartFacts facts = WindowsStartup.GetFacts();

        // Moved, say into C:\Program Files: flag, Run entry and task still name the old path.
        if (WindowsStartup.FollowMove(facts))
        {
            facts = WindowsStartup.GetFacts();
        }

        // An earlier version removed the compatibility flag when it created the elevated task,
        // which silently stopped manual starts from asking for administrator rights.
        if (AutostartStatus.NeedsFlagRestore(facts) && WindowsStartup.SetRunAsAdminFlag(true))
        {
            facts = WindowsStartup.GetFacts();
        }

        (bool admin, bool autostart) = AutostartStatus.ReadIntent(facts);

        // Flag plus Run entry means a dead autostart: Windows skips the entry. Running elevated,
        // the task can be registered without a prompt, so it is fixed right here.
        if (admin && autostart && !facts.HasScheduledTask && ElevationHelper.IsElevated &&
            WindowsStartup.Apply(AutostartMode.Elevated))
        {
            facts = WindowsStartup.GetFacts();
        }

        AdoptFromSystem(facts);
    }

    /// <summary>
    /// Applies both switches: the compatibility flag, which makes manual starts ask for
    /// administrator rights, and the autostart mechanism that fits them.
    /// </summary>
    private void ApplyStartup(bool runAsAdministrator, bool startWithWindows)
    {
        AutostartMode mode = startWithWindows
            ? (runAsAdministrator ? AutostartMode.Elevated : AutostartMode.Standard)
            : AutostartMode.Disabled;

        AutostartFacts before = WindowsStartup.GetFacts();
        bool mechanismInPlace = AutostartStatus.ResolveMode(before) == mode &&
                                AutostartStatus.ResolveState(before) != AutostartState.Conflicting;

        bool applied = mechanismInPlace || WindowsStartup.Apply(mode);

        if (!applied)
        {
            MessageBox.Show(
                (mode == AutostartMode.Elevated
                    ? "Die geplante Aufgabe für den Autostart mit Administratorrechten konnte nicht angelegt werden."
                    : "Der Autostart konnte nicht geändert werden.") +
                "\n\n" + (ScheduledTaskAutostart.LastError ?? "Ursache unbekannt.") +
                "\n\nDie bisherigen Einstellungen bleiben bestehen. Details stehen in der Protokolldatei im Datenordner.",
                "PowerPlugin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (runAsAdministrator)
        {
            // Only once the task is in place: next to a plain Run entry the flag would make
            // Windows skip that entry and break an autostart that works today. A flag the user
            // already had is never taken away.
            if (!before.RunAsAdminFlagSet && (mode != AutostartMode.Elevated || applied))
            {
                WindowsStartup.SetRunAsAdminFlag(true);
            }
        }
        else if (before.RunAsAdminFlagSet)
        {
            WindowsStartup.SetRunAsAdminFlag(false);
        }

        AdoptFromSystem(WindowsStartup.GetFacts());
    }

    /// <summary>Shows what is actually in effect, which after a failure is not what was asked for.</summary>
    private void AdoptFromSystem(AutostartFacts facts)
    {
        (bool admin, bool autostart) = AutostartStatus.ReadIntent(facts);

        if (admin != _settings.RunAsAdministrator || autostart != _settings.StartWithWindows)
        {
            _settings.RunAsAdministrator = admin;
            _settings.StartWithWindows = autostart;
            _settingsStore.Save(_settings);
        }

        _window.ApplySettings(_settings);
        PublishAutostartState(facts);
    }

    private void PublishAutostartState(AutostartFacts facts)
    {
        _tray.SetAutostartState(AutostartStatus.ResolveMode(facts) != AutostartMode.Disabled);
        _window.SetAutostartStatus(facts);
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

    /// <summary>
    /// Writes every sensor the library sees, together with what PowerPlugin makes of it, to a text
    /// file in the data folder and opens it. When a reading is missing although PawnIO and
    /// administrator rights are both there, this shows the sensor names actually present.
    /// </summary>
    private void CreateSensorReport()
    {
        string path = Path.Combine(AppPaths.DataDirectory, "sensorbericht.txt");

        try
        {
            string access = _monitor.CpuSensorAccess switch
            {
                SensorAccessState.Available => "gemessen",
                SensorAccessState.NeedsAdministrator => "geschätzt - Administratorrechte fehlen",
                SensorAccessState.NeedsHelperDriver => "geschätzt - PawnIO nicht installiert",
                _ => "geschätzt - kein Package-Sensor gefunden",
            };

            var header = new System.Text.StringBuilder()
                .AppendLine("PowerPlugin - Sensorbericht")
                .AppendLine($"Erstellt:            {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"Windows:             {Environment.OSVersion.VersionString}")
                .AppendLine($"Administratorrechte: {(ElevationHelper.IsElevated ? "ja" : "nein")}")
                .AppendLine($"PawnIO:              {(HelperDriver.IsInstalled ? "installiert, Version " + (HelperDriver.InstalledVersion ?? "unbekannt") : "nicht installiert")}")
                .AppendLine($"CPU-Leistung:        {access}")
                .AppendLine($"Gesuchte CPU-Sensoren:        {string.Join(", ", LibreHardwareTelemetryProvider.CpuPackageSensorNames)}")
                .AppendLine($"Gesuchte Grafikkarten-Sensoren: {string.Join(", ", LibreHardwareTelemetryProvider.GpuPowerSensorNames)}")
                .AppendLine()
                .AppendLine("Alle Sensoren, die die Sensorbibliothek sieht (Typ, Name, Wert):")
                .AppendLine();

            File.WriteAllText(path, header + _provider.BuildSensorReport());
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            DiagnosticsLog.Write($"Sensorbericht geschrieben: {path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DiagnosticsLog.Write("Sensorbericht", exception);
            MessageBox.Show(
                $"Der Sensorbericht konnte nicht erstellt werden:\n\n{exception.Message}",
                "PowerPlugin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

        // The new instance waits until this one has let go of the single instance mutex.
        if (ElevationHelper.TryRestartElevated($"{Program.RestartedArgument} {Program.ShowWindowArgument}"))
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
