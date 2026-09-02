using PowerPlugin.Core.Model;
using PowerPlugin.Core.Storage;
using Xunit;

namespace PowerPlugin.Tests;

public sealed class SqliteEnergyStoreTests : IDisposable
{
    private static readonly DateTimeOffset Minute = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 3, 14);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"powerplugin-test-{Guid.NewGuid():N}.db");

    private readonly SqliteEnergyStore _store;

    public SqliteEnergyStoreTests()
    {
        _store = new SqliteEnergyStore(_databasePath);
        _store.Initialize();
    }

    private static EnergyBucket Bucket(
        DateTimeOffset start,
        double energyWh = 1.0,
        double coveredSeconds = 60,
        double peak = 100,
        double min = 50,
        int samples = 30) =>
        new(start, DayKey.Encode(DateOnly.FromDateTime(start.UtcDateTime)), energyWh, coveredSeconds, peak, min, samples);

    [Fact]
    public void InitializeIsIdempotent()
    {
        _store.Initialize();
        _store.Initialize();

        Assert.Equal(StoreTotals.Empty, _store.GetTotals());
    }

    [Fact]
    public void BucketsSurviveARoundTrip()
    {
        _store.UpsertBucket(Bucket(Minute, energyWh: 1.5));

        EnergyBucket stored = Assert.Single(_store.GetBuckets(Minute.AddMinutes(-1), Minute.AddMinutes(1)));

        Assert.Equal(1.5, stored.EnergyWattHours, precision: 6);
        Assert.Equal(60, stored.CoveredSeconds, precision: 6);
        Assert.Equal(Minute, stored.StartUtc);
    }

    [Fact]
    public void WritingTheSameMinuteTwiceMergesAdditively()
    {
        _store.UpsertBucket(Bucket(Minute, energyWh: 1.0, coveredSeconds: 30, peak: 120, min: 80, samples: 15));
        _store.UpsertBucket(Bucket(Minute, energyWh: 0.5, coveredSeconds: 30, peak: 90, min: 40, samples: 15));

        EnergyBucket stored = Assert.Single(_store.GetBuckets(Minute, Minute));

        Assert.Equal(1.5, stored.EnergyWattHours, precision: 6);
        Assert.Equal(60, stored.CoveredSeconds, precision: 6);
        Assert.Equal(120, stored.PeakWatts);   // maximum wins
        Assert.Equal(40, stored.MinWatts);     // minimum wins
        Assert.Equal(30, stored.SampleCount);  // counts add up
    }

    [Fact]
    public void DailyAggregationSumsEveryMinuteOfTheDay()
    {
        for (int i = 0; i < 10; i++)
        {
            _store.UpsertBucket(Bucket(Minute.AddMinutes(i), energyWh: 2.0, peak: 100 + i));
        }

        DailyEnergy day = Assert.Single(_store.GetDailyEnergy(Day, Day));

        Assert.Equal(20.0, day.EnergyWattHours, precision: 6);
        Assert.Equal(600, day.CoveredSeconds, precision: 6);
        Assert.Equal(109, day.PeakWatts);
    }

    [Fact]
    public void DailyQueryRespectsItsRange()
    {
        _store.UpsertBucket(Bucket(Minute.AddDays(-2), energyWh: 5));
        _store.UpsertBucket(Bucket(Minute, energyWh: 7));

        IReadOnlyList<DailyEnergy> days = _store.GetDailyEnergy(Day, Day);

        Assert.Single(days);
        Assert.Equal(7, days[0].EnergyWattHours, precision: 6);
    }

    [Fact]
    public void TotalsDescribeTheWholeHistory()
    {
        _store.UpsertBucket(Bucket(Minute.AddDays(-3), energyWh: 4));
        _store.UpsertBucket(Bucket(Minute, energyWh: 6));

        StoreTotals totals = _store.GetTotals();

        Assert.Equal(10, totals.EnergyWattHours, precision: 6);
        Assert.Equal(120, totals.CoveredSeconds, precision: 6);
        Assert.Equal(Day.AddDays(-3), totals.FirstDay);
        Assert.Equal(Day, totals.LastDay);
        Assert.Equal(2, totals.DaysWithData);
    }

    [Fact]
    public void AllTimePeakReturnsTheHighestSampleAndItsTimestamp()
    {
        _store.UpsertBucket(Bucket(Minute, peak: 210));
        _store.UpsertBucket(Bucket(Minute.AddMinutes(1), peak: 640));
        _store.UpsertBucket(Bucket(Minute.AddMinutes(2), peak: 310));

        PeakRecord? peak = _store.GetAllTimePeak();

        Assert.NotNull(peak);
        Assert.Equal(640, peak!.Watts);
        Assert.Equal(Minute.AddMinutes(1), peak.AtUtc);
    }

    [Fact]
    public void ComponentEnergyIsMergedAndOrderedByShare()
    {
        _store.UpsertComponentEnergy(Minute,
        [
            new ComponentEnergy("cpu", "CPU", (int)ComponentCategory.Cpu, 10, 3600),
            new ComponentEnergy("gpu", "GPU", (int)ComponentCategory.Gpu, 30, 3600),
        ]);

        _store.UpsertComponentEnergy(Minute, [new ComponentEnergy("cpu", "CPU", (int)ComponentCategory.Cpu, 5, 3600)]);

        IReadOnlyList<ComponentEnergy> components =
            _store.GetComponentEnergy(Minute.AddHours(-1), Minute.AddHours(1));

        Assert.Equal(2, components.Count);
        Assert.Equal("gpu", components[0].Key);
        Assert.Equal(30, components[0].EnergyWattHours, precision: 6);
        Assert.Equal(15, components[1].EnergyWattHours, precision: 6);
    }

    [Fact]
    public void PurgingRemovesOnlyTheOlderRows()
    {
        _store.UpsertBucket(Bucket(Minute.AddDays(-10), energyWh: 3));
        _store.UpsertBucket(Bucket(Minute, energyWh: 4));
        _store.UpsertComponentEnergy(Minute.AddDays(-10), [new ComponentEnergy("cpu", "CPU", 0, 3, 60)]);

        int removed = _store.PurgeOlderThan(Minute.AddDays(-5));

        Assert.Equal(1, removed);
        Assert.Equal(4, _store.GetTotals().EnergyWattHours, precision: 6);
        Assert.Empty(_store.GetComponentEnergy(Minute.AddDays(-30), Minute.AddDays(1)));
    }

    [Fact]
    public void ClearEmptiesEverything()
    {
        _store.UpsertBucket(Bucket(Minute, energyWh: 4));
        _store.UpsertComponentEnergy(Minute, [new ComponentEnergy("cpu", "CPU", 0, 3, 60)]);

        _store.Clear();

        Assert.Equal(StoreTotals.Empty, _store.GetTotals());
        Assert.Null(_store.GetAllTimePeak());
    }

    [Fact]
    public void DataOutlivesTheConnection()
    {
        _store.UpsertBucket(Bucket(Minute, energyWh: 9));
        _store.Dispose();

        using var reopened = new SqliteEnergyStore(_databasePath);
        reopened.Initialize();

        Assert.Equal(9, reopened.GetTotals().EnergyWattHours, precision: 6);
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 12, 31)]
    [InlineData(2024, 2, 29)]
    public void DayKeysRoundTrip(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        Assert.Equal(date, DayKey.Decode(DayKey.Encode(date)));
    }

    public void Dispose()
    {
        _store.Dispose();

        foreach (string path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temporary file is not worth failing a test over.
            }
        }
    }
}
