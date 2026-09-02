using System.Collections.Immutable;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Storage;
using Xunit;

namespace PowerPlugin.Tests;

public sealed class EnergyRecorderTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);

    private static PowerSnapshot Snapshot(DateTimeOffset at, double watts) => new(
        at,
        [new PowerComponent("cpu", "CPU", ComponentCategory.Cpu, watts, PowerReadingSource.Estimated)],
        belowThresholdWatts: 0,
        conversionLossWatts: 0,
        measuredSystemWatts: null,
        confidence: MeasurementConfidence.Low);

    [Fact]
    public void EnergyIsIntegratedOverTheSampleInterval()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(10));

        recorder.Record(Snapshot(Start, 100));
        recorder.Record(Snapshot(Start.AddSeconds(10), 100));
        recorder.Record(Snapshot(Start.AddSeconds(20), 100));
        recorder.Flush();

        // The first sample seeds one interval, the two following ones add ten seconds each.
        Assert.Equal(30, store.TotalCoveredSeconds, precision: 3);
        Assert.Equal(100 * 30 / 3600.0, store.TotalEnergyWattHours, precision: 6);
    }

    [Fact]
    public void ChangingPowerIsHeldUntilTheNextSample()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(10));

        recorder.Record(Snapshot(Start, 100));                  // seeds 10 s at 100 W
        recorder.Record(Snapshot(Start.AddSeconds(10), 200));   // 10 s at 200 W
        recorder.Flush();

        Assert.Equal((100 * 10 / 3600.0) + (200 * 10 / 3600.0), store.TotalEnergyWattHours, precision: 6);
    }

    [Fact]
    public void StandbyGapsAreNotIntegratedAsConsumption()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(10));

        recorder.Record(Snapshot(Start, 100));
        recorder.Record(Snapshot(Start.AddHours(8), 100));  // machine slept for eight hours
        recorder.Flush();

        // Only two intervals may be counted, not eight hours worth of energy.
        Assert.Equal(20, store.TotalCoveredSeconds, precision: 3);
        Assert.True(store.TotalEnergyWattHours < 1);
    }

    [Fact]
    public void ResumingAfterStandbyRestartsTheTimeBase()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(10));

        recorder.Record(Snapshot(Start, 100));
        recorder.ResetTiming();
        recorder.Record(Snapshot(Start.AddSeconds(25), 100));
        recorder.Flush();

        Assert.Equal(20, store.TotalCoveredSeconds, precision: 3);
    }

    [Fact]
    public void ShortDelaysAtFastSamplingRatesAreStillCounted()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(0.5));

        // Three times the interval would be 1.5 s, which a busy machine exceeds routinely.
        // A delay of a few seconds is jitter, not standby, and its energy must not be dropped.
        recorder.Record(Snapshot(Start, 100));
        recorder.Record(Snapshot(Start.AddSeconds(4), 100));
        recorder.Flush();

        Assert.Equal(4.5, store.TotalCoveredSeconds, precision: 3);
    }

    [Fact]
    public void RealStandbyIsStillClampedAtAFastSamplingRate()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(0.5));

        recorder.Record(Snapshot(Start, 100));
        recorder.Record(Snapshot(Start.AddMinutes(30), 100));
        recorder.Flush();

        Assert.Equal(1.0, store.TotalCoveredSeconds, precision: 3);
    }

    [Fact]
    public void PeakAndMinimumAreTracked()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(5));

        recorder.Record(Snapshot(Start, 90));
        recorder.Record(Snapshot(Start.AddSeconds(5), 310));
        recorder.Record(Snapshot(Start.AddSeconds(10), 120));
        recorder.Flush();

        EnergyBucket bucket = Assert.Single(store.Buckets);
        Assert.Equal(310, bucket.PeakWatts);
        Assert.Equal(90, bucket.MinWatts);
        Assert.Equal(3, bucket.SampleCount);
    }

    [Fact]
    public void EachMinuteBecomesItsOwnBucket()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(30));

        recorder.Record(Snapshot(Start, 100));
        recorder.Record(Snapshot(Start.AddSeconds(30), 100));
        recorder.Record(Snapshot(Start.AddSeconds(60), 100));   // next minute
        recorder.Record(Snapshot(Start.AddSeconds(90), 100));
        recorder.Flush();

        Assert.Equal(2, store.Buckets.Count);
        Assert.All(store.Buckets, b => Assert.Equal(0, b.StartUtc.Second));
    }

    [Fact]
    public void FlushingTwiceDoesNotCountTheSameEnergyAgain()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(10));

        recorder.Record(Snapshot(Start, 100));
        recorder.Flush();
        recorder.Flush();

        Assert.Equal(100 * 10 / 3600.0, store.TotalEnergyWattHours, precision: 6);
    }

    [Fact]
    public void PartialFlushesAddUpToTheSameTotal()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(10));

        // The statistics refresh flushes mid-minute; the store merges additively.
        recorder.Record(Snapshot(Start, 100));
        recorder.Flush();
        recorder.Record(Snapshot(Start.AddSeconds(10), 100));
        recorder.Flush();

        Assert.Equal(20, store.TotalCoveredSeconds, precision: 3);
        Assert.Equal(100 * 20 / 3600.0, store.TotalEnergyWattHours, precision: 6);
    }

    [Fact]
    public void ComponentEnergyIsWrittenPerHour()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(30));

        recorder.Record(Snapshot(Start, 100));
        recorder.Flush();

        (DateTimeOffset hour, IReadOnlyCollection<ComponentEnergy> components) = Assert.Single(store.ComponentWrites);

        Assert.Equal(0, hour.Minute);
        ComponentEnergy component = Assert.Single(components);
        Assert.Equal("cpu", component.Key);
        Assert.Equal(100 * 30 / 3600.0, component.EnergyWattHours, precision: 6);
    }

    [Fact]
    public void SamplesWithoutComponentsStillRecordTheTotal()
    {
        var store = new FakeEnergyStore();
        var recorder = new EnergyRecorder(store, TimeSpan.FromSeconds(10));

        var snapshot = new PowerSnapshot(
            Start,
            ImmutableArray<PowerComponent>.Empty,
            belowThresholdWatts: 42,
            conversionLossWatts: 0,
            measuredSystemWatts: null,
            confidence: MeasurementConfidence.Low);

        recorder.Record(snapshot);
        recorder.Flush();

        Assert.Equal(42 * 10 / 3600.0, store.TotalEnergyWattHours, precision: 6);
    }
}
