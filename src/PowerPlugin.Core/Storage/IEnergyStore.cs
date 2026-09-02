namespace PowerPlugin.Core.Storage;

/// <summary>
/// Persistence for the aggregated consumption history.
/// </summary>
public interface IEnergyStore : IDisposable
{
    /// <summary>Creates the schema if needed. Safe to call more than once.</summary>
    void Initialize();

    /// <summary>
    /// Adds a minute bucket. An existing bucket for the same minute is merged additively so
    /// a restart within the same minute never loses or double counts data.
    /// </summary>
    void UpsertBucket(EnergyBucket bucket);

    /// <summary>Adds per component energy for one hour, merged additively.</summary>
    void UpsertComponentEnergy(DateTimeOffset hourStartUtc, IReadOnlyCollection<ComponentEnergy> components);

    /// <summary>Minute buckets within a UTC range, ordered by time.</summary>
    IReadOnlyList<EnergyBucket> GetBuckets(DateTimeOffset fromUtc, DateTimeOffset toUtc);

    /// <summary>Daily aggregates for the local calendar days in the inclusive range.</summary>
    IReadOnlyList<DailyEnergy> GetDailyEnergy(DateOnly fromDay, DateOnly toDay);

    /// <summary>Per component energy over a UTC range.</summary>
    IReadOnlyList<ComponentEnergy> GetComponentEnergy(DateTimeOffset fromUtc, DateTimeOffset toUtc);

    /// <summary>Highest single sample ever recorded, if any.</summary>
    PeakRecord? GetAllTimePeak();

    StoreTotals GetTotals();

    /// <summary>Removes buckets older than the retention window. Returns the number of deleted rows.</summary>
    int PurgeOlderThan(DateTimeOffset cutoffUtc);

    /// <summary>Deletes the entire history.</summary>
    void Clear();
}
