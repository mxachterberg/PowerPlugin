using PowerPlugin.Core.Storage;

namespace PowerPlugin.Tests;

/// <summary>In-memory store that records what the recorder hands over.</summary>
internal sealed class FakeEnergyStore : IEnergyStore
{
    public List<EnergyBucket> Buckets { get; } = [];

    public List<(DateTimeOffset Hour, IReadOnlyCollection<ComponentEnergy> Components)> ComponentWrites { get; } = [];

    public double TotalEnergyWattHours => Buckets.Sum(b => b.EnergyWattHours);

    public double TotalCoveredSeconds => Buckets.Sum(b => b.CoveredSeconds);

    public double PeakWatts => Buckets.Count == 0 ? 0 : Buckets.Max(b => b.PeakWatts);

    public void Initialize()
    {
    }

    public void UpsertBucket(EnergyBucket bucket) => Buckets.Add(bucket);

    public void UpsertComponentEnergy(DateTimeOffset hourStartUtc, IReadOnlyCollection<ComponentEnergy> components) =>
        ComponentWrites.Add((hourStartUtc, components));

    public IReadOnlyList<EnergyBucket> GetBuckets(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        Buckets.Where(b => b.StartUtc >= fromUtc && b.StartUtc <= toUtc).ToList();

    public IReadOnlyList<DailyEnergy> GetDailyEnergy(DateOnly fromDay, DateOnly toDay) =>
        Buckets
            .GroupBy(b => DayKey.Decode(b.LocalDay))
            .Where(g => g.Key >= fromDay && g.Key <= toDay)
            .Select(g => new DailyEnergy(
                g.Key,
                g.Sum(b => b.EnergyWattHours),
                g.Sum(b => b.CoveredSeconds),
                g.Max(b => b.PeakWatts)))
            .OrderBy(d => d.Day)
            .ToList();

    public IReadOnlyList<ComponentEnergy> GetComponentEnergy(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        ComponentWrites
            .Where(w => w.Hour >= fromUtc && w.Hour <= toUtc)
            .SelectMany(w => w.Components)
            .GroupBy(c => c.Key)
            .Select(g => new ComponentEnergy(
                g.Key,
                g.First().Name,
                g.First().Category,
                g.Sum(c => c.EnergyWattHours),
                g.Sum(c => c.CoveredSeconds)))
            .ToList();

    public PeakRecord? GetAllTimePeak() => Buckets.Count == 0
        ? null
        : Buckets.OrderByDescending(b => b.PeakWatts)
            .Select(b => new PeakRecord(b.PeakWatts, b.StartUtc))
            .First();

    public StoreTotals GetTotals()
    {
        if (Buckets.Count == 0)
        {
            return StoreTotals.Empty;
        }

        List<DateOnly> days = Buckets.Select(b => DayKey.Decode(b.LocalDay)).Distinct().OrderBy(d => d).ToList();

        return new StoreTotals(
            Buckets.Sum(b => b.EnergyWattHours),
            Buckets.Sum(b => b.CoveredSeconds),
            days.First(),
            days.Last(),
            days.Count);
    }

    public int PurgeOlderThan(DateTimeOffset cutoffUtc) => Buckets.RemoveAll(b => b.StartUtc < cutoffUtc);

    public void Clear()
    {
        Buckets.Clear();
        ComponentWrites.Clear();
    }

    public void Dispose()
    {
    }
}
