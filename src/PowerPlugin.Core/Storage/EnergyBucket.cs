namespace PowerPlugin.Core.Storage;

/// <summary>
/// Aggregated consumption of one minute. Storing buckets instead of raw samples keeps a
/// year of history at roughly half a million rows while preserving peaks.
/// </summary>
/// <param name="StartUtc">Start of the minute in UTC.</param>
/// <param name="LocalDay">Calendar day in local time, encoded as yyyyMMdd.</param>
/// <param name="EnergyWattHours">Energy accumulated in this minute.</param>
/// <param name="CoveredSeconds">Seconds actually covered by samples - less than 60 across a gap.</param>
/// <param name="PeakWatts">Highest single sample in this minute.</param>
/// <param name="MinWatts">Lowest single sample in this minute.</param>
/// <param name="SampleCount">Number of samples that contributed.</param>
public sealed record EnergyBucket(
    DateTimeOffset StartUtc,
    int LocalDay,
    double EnergyWattHours,
    double CoveredSeconds,
    double PeakWatts,
    double MinWatts,
    int SampleCount)
{
    public double AverageWatts => CoveredSeconds > 0 ? EnergyWattHours * 3600.0 / CoveredSeconds : 0;
}

/// <summary>Consumption of one calendar day in local time.</summary>
public sealed record DailyEnergy(
    DateOnly Day,
    double EnergyWattHours,
    double CoveredSeconds,
    double PeakWatts)
{
    public double EnergyKilowattHours => EnergyWattHours / 1000.0;

    /// <summary>Average power while the machine was actually running.</summary>
    public double AverageWatts => CoveredSeconds > 0 ? EnergyWattHours * 3600.0 / CoveredSeconds : 0;

    public double CoveredHours => CoveredSeconds / 3600.0;

    public static DailyEnergy Zero(DateOnly day) => new(day, 0, 0, 0);
}

/// <summary>Energy attributed to a single component over a period.</summary>
public sealed record ComponentEnergy(string Key, string Name, int Category, double EnergyWattHours, double CoveredSeconds)
{
    public double AverageWatts => CoveredSeconds > 0 ? EnergyWattHours * 3600.0 / CoveredSeconds : 0;
}

/// <summary>The highest single sample ever recorded.</summary>
public sealed record PeakRecord(double Watts, DateTimeOffset AtUtc);

/// <summary>Aggregate over the whole database.</summary>
public sealed record StoreTotals(
    double EnergyWattHours,
    double CoveredSeconds,
    DateOnly? FirstDay,
    DateOnly? LastDay,
    int DaysWithData)
{
    public static StoreTotals Empty { get; } = new(0, 0, null, null, 0);
}
