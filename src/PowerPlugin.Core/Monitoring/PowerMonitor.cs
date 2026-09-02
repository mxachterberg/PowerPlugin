using System.Collections.Immutable;
using PowerPlugin.Core.Estimation;
using PowerPlugin.Core.Hardware;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Storage;

namespace PowerPlugin.Core.Monitoring;

public sealed class SnapshotEventArgs(PowerSnapshot snapshot) : EventArgs
{
    public PowerSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// The sampling loop: reads the sensors, runs the estimation model, publishes the result and
/// hands it to the recorder. Runs on a background thread; consumers marshal to their UI thread.
/// </summary>
public sealed class PowerMonitor : IDisposable
{
    /// <summary>Length of the in-memory history used by the live chart.</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(10);

    private readonly IHardwareTelemetryProvider _provider;
    private readonly EnergyRecorder _recorder;
    private readonly object _gate = new();
    private readonly Queue<PowerSnapshot> _live = new();

    private ComponentPowerEstimator _estimator;
    private HardwareInventory _inventory = HardwareInventory.Fallback;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private PeriodicTimer? _timer;
    private TimeSpan _interval;
    private int _consecutiveFailures;
    private bool _disposed;

    public PowerMonitor(
        IHardwareTelemetryProvider provider,
        ComponentPowerEstimator estimator,
        EnergyRecorder recorder,
        TimeSpan sampleInterval)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _interval = sampleInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : sampleInterval;
    }

    /// <summary>Raised after every successful sample.</summary>
    public event EventHandler<SnapshotEventArgs>? SnapshotUpdated;

    /// <summary>Raised when reading the sensors failed.</summary>
    public event EventHandler<string>? SamplingFailed;

    public PowerSnapshot Current { get; private set; } = PowerSnapshot.Empty;

    public HardwareInventory Inventory => _inventory;

    /// <summary>True when the sensor backend reported that it needs administrator rights.</summary>
    public bool RequiresElevation { get; private set; }

    public TimeSpan SampleInterval
    {
        get => _interval;
        set
        {
            _interval = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : value;
            _recorder.SampleInterval = _interval;

            // Apply the new rate to the running loop instead of waiting for a restart.
            if (_timer is { } timer)
            {
                timer.Period = _interval;
            }
        }
    }

    /// <summary>Recent snapshots for the live chart, oldest first.</summary>
    public ImmutableArray<PowerSnapshot> LiveHistory
    {
        get
        {
            lock (_gate)
            {
                return _live.ToImmutableArray();
            }
        }
    }

    /// <summary>Swaps the estimator, e.g. after the user changed a model coefficient.</summary>
    public void UpdateEstimator(ComponentPowerEstimator estimator) =>
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loop is not null)
        {
            return;
        }

        try
        {
            _inventory = _provider.GetInventory();
            DiagnosticsLog.Write(
                $"Hardware erkannt: CPU='{_inventory.Cpu.Name}' ({_inventory.Cpu.PhysicalCores} Kerne), " +
                $"GPUs={_inventory.Gpus.Count}, RAM-Module={_inventory.MemoryModules.Count}, " +
                $"Laufwerke={_inventory.StorageDevices.Count}, Mobil={_inventory.IsMobileSystem}");
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Hardware-Inventarisierung fehlgeschlagen", exception);
            _inventory = HardwareInventory.Fallback;
        }

        _cancellation = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cancellation.Token));
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation = _cancellation;
        Task? loop = _loop;

        _cancellation = null;
        _loop = null;

        if (cancellation is null || loop is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }

        _recorder.Flush();
    }

    /// <summary>
    /// Called after the machine resumed from standby so the gap is not integrated as consumption.
    /// </summary>
    public void NotifyResumed()
    {
        _recorder.ResetTiming();
        DiagnosticsLog.Write("Betrieb nach Standby fortgesetzt - Zeitbasis zurückgesetzt.");
    }

    /// <summary>Flushes buffered energy to disk, e.g. before the machine suspends.</summary>
    public void FlushToDisk() => _recorder.Flush();

    private async Task RunAsync(CancellationToken token)
    {
        // Take one sample immediately so the tray does not show a placeholder on startup.
        SampleOnce();

        using var timer = new PeriodicTimer(_interval);
        _timer = timer;

        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                SampleOnce();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _timer = null;
        }
    }

    private void SampleOnce()
    {
        try
        {
            HardwareTelemetry telemetry = _provider.Read();
            RequiresElevation = telemetry.RequiresElevation;

            PowerSnapshot snapshot = _estimator.Estimate(_inventory, telemetry, DateTimeOffset.Now);

            lock (_gate)
            {
                Current = snapshot;
                _live.Enqueue(snapshot);

                DateTimeOffset cutoff = snapshot.Timestamp - LiveWindow;
                while (_live.Count > 0 && _live.Peek().Timestamp < cutoff)
                {
                    _live.Dequeue();
                }
            }

            _recorder.Record(snapshot);
            _consecutiveFailures = 0;

            SnapshotUpdated?.Invoke(this, new SnapshotEventArgs(snapshot));
        }
        catch (Exception exception)
        {
            _consecutiveFailures++;

            // Only the first failures are logged, otherwise a permanently broken sensor
            // would fill the log file within minutes.
            if (_consecutiveFailures <= 3)
            {
                DiagnosticsLog.Write("Sensorabfrage fehlgeschlagen", exception);
                SamplingFailed?.Invoke(this, exception.Message);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Beenden des Monitors", exception);
        }

        _provider.Dispose();
    }
}
