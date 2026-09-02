using PowerPlugin.Core.Model;

namespace PowerPlugin.Core.Storage;

/// <summary>
/// Accumulates live snapshots into minute buckets and writes them to the store.
/// <para>
/// Energy is integrated with a zero order hold: every sample is assumed to represent the
/// interval since the previous sample. Gaps that are much longer than the sampling interval -
/// standby, hibernation, a stopped process - are clamped, because the machine almost certainly
/// did not draw the last observed power for the whole gap.
/// </para>
/// </summary>
public sealed class EnergyRecorder
{
    private readonly IEnergyStore _store;
    private readonly object _gate = new();

    private readonly Dictionary<string, ComponentAccumulator> _componentEnergy = new(StringComparer.Ordinal);

    private DateTimeOffset? _lastSampleUtc;
    private DateTimeOffset _currentMinute;
    private DateTimeOffset _currentHour;

    private double _energyWattHours;
    private double _coveredSeconds;
    private double _peakWatts;
    private double _minWatts;
    private int _sampleCount;

    public EnergyRecorder(IEnergyStore store, TimeSpan sampleInterval)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        SampleInterval = sampleInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : sampleInterval;
        ResetBucket(DateTimeOffset.UnixEpoch);
        _currentHour = DateTimeOffset.UnixEpoch;
    }

    public TimeSpan SampleInterval { get; set; }

    /// <summary>
    /// Gaps longer than this are treated as downtime. The clamped interval is still counted so a
    /// short hiccup does not lose energy, but a night in standby cannot inflate the statistics.
    /// <para>
    /// The floor matters at fast sampling rates: at half a second, three intervals are 1.5 s, and
    /// a garbage collection or a busy machine can easily delay a sample that long. Treating that
    /// as downtime would quietly lose energy, while a real standby lasts minutes at the very least.
    /// </para>
    /// </summary>
    public TimeSpan MaximumGap => TimeSpan.FromTicks(
        Math.Max(SampleInterval.Ticks * 3, MinimumGapAllowance.Ticks));

    /// <summary>Lower bound for <see cref="MaximumGap"/>, see there.</summary>
    private static readonly TimeSpan MinimumGapAllowance = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Called after resuming from standby so the next sample does not integrate over the gap.
    /// </summary>
    public void ResetTiming()
    {
        lock (_gate)
        {
            _lastSampleUtc = null;
        }
    }

    public void Record(PowerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            DateTimeOffset nowUtc = snapshot.Timestamp.ToUniversalTime();
            double watts = Math.Max(0, snapshot.TotalWatts);

            double deltaSeconds = ResolveDeltaSeconds(nowUtc);
            _lastSampleUtc = nowUtc;

            DateTimeOffset minute = FloorTo(nowUtc, TimeSpan.FromMinutes(1));
            if (minute != _currentMinute)
            {
                FlushBucketCore();
                ResetBucket(minute);
            }

            DateTimeOffset hour = FloorTo(nowUtc, TimeSpan.FromHours(1));
            if (hour != _currentHour)
            {
                FlushComponentsCore();
                _currentHour = hour;
            }

            _energyWattHours += watts * deltaSeconds / 3600.0;
            _coveredSeconds += deltaSeconds;
            _peakWatts = Math.Max(_peakWatts, watts);
            _minWatts = _sampleCount == 0 ? watts : Math.Min(_minWatts, watts);
            _sampleCount++;

            AccumulateComponents(snapshot, deltaSeconds);
        }
    }

    /// <summary>Writes everything buffered so far. Call before shutting down.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            FlushBucketCore();
            FlushComponentsCore();
            // Keep accumulating into the same minute after a manual flush; the store merges
            // additively, so a partially written bucket is completed by the next flush.
            _energyWattHours = 0;
            _coveredSeconds = 0;
            _sampleCount = 0;
            _peakWatts = 0;
        }
    }

    private double ResolveDeltaSeconds(DateTimeOffset nowUtc)
    {
        if (_lastSampleUtc is not { } previous)
        {
            // First sample after start or resume: attribute a single interval instead of nothing,
            // otherwise short sessions would record no energy at all.
            return SampleInterval.TotalSeconds;
        }

        double delta = (nowUtc - previous).TotalSeconds;

        if (delta <= 0)
        {
            return 0;
        }

        return delta > MaximumGap.TotalSeconds ? SampleInterval.TotalSeconds : delta;
    }

    private void AccumulateComponents(PowerSnapshot snapshot, double deltaSeconds)
    {
        foreach (PowerComponent component in snapshot.Components)
        {
            if (!_componentEnergy.TryGetValue(component.Key, out ComponentAccumulator? accumulator))
            {
                accumulator = new ComponentAccumulator(component.Name, (int)component.Category);
                _componentEnergy[component.Key] = accumulator;
            }

            accumulator.Name = component.Name;
            accumulator.EnergyWattHours += component.Watts * deltaSeconds / 3600.0;
            accumulator.CoveredSeconds += deltaSeconds;
        }
    }

    private void FlushBucketCore()
    {
        if (_sampleCount == 0 || _currentMinute == DateTimeOffset.UnixEpoch)
        {
            return;
        }

        _store.UpsertBucket(new EnergyBucket(
            _currentMinute,
            DayKey.Encode(DateOnly.FromDateTime(_currentMinute.ToLocalTime().DateTime)),
            _energyWattHours,
            _coveredSeconds,
            _peakWatts,
            _minWatts,
            _sampleCount));
    }

    private void FlushComponentsCore()
    {
        if (_componentEnergy.Count == 0 || _currentHour == DateTimeOffset.UnixEpoch)
        {
            return;
        }

        var payload = _componentEnergy
            .Select(pair => new ComponentEnergy(
                pair.Key,
                pair.Value.Name,
                pair.Value.Category,
                pair.Value.EnergyWattHours,
                pair.Value.CoveredSeconds))
            .ToList();

        _store.UpsertComponentEnergy(_currentHour, payload);
        _componentEnergy.Clear();
    }

    private void ResetBucket(DateTimeOffset minute)
    {
        _currentMinute = minute;
        _energyWattHours = 0;
        _coveredSeconds = 0;
        _peakWatts = 0;
        _minWatts = 0;
        _sampleCount = 0;
    }

    private static DateTimeOffset FloorTo(DateTimeOffset value, TimeSpan resolution) =>
        new(value.Ticks - (value.Ticks % resolution.Ticks), value.Offset);

    private sealed class ComponentAccumulator(string name, int category)
    {
        public string Name { get; set; } = name;

        public int Category { get; } = category;

        public double EnergyWattHours { get; set; }

        public double CoveredSeconds { get; set; }
    }
}
