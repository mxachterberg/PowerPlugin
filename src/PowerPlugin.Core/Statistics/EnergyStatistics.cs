using PowerPlugin.Core.Storage;

namespace PowerPlugin.Core.Statistics;

/// <summary>
/// How much history the forecast is based on. Shown next to the annual estimate so a
/// number extrapolated from ten minutes is not mistaken for a measured value.
/// </summary>
public enum ForecastQuality
{
    /// <summary>Less than one full day of data - the annual figure is a rough indication.</summary>
    VeryLow,

    /// <summary>At least one, but less than seven days.</summary>
    Low,

    /// <summary>At least a week of data.</summary>
    Medium,

    /// <summary>A month or more.</summary>
    High,
}

/// <summary>Aggregated values for one period, e.g. today or the current month.</summary>
/// <param name="EnergyKilowattHours">Energy consumed in the period.</param>
/// <param name="AverageWatts">Average power while the machine was running.</param>
/// <param name="PeakWatts">Highest single sample in the period.</param>
/// <param name="CoveredHours">Hours the monitor actually observed.</param>
public sealed record PeriodStatistics(
    double EnergyKilowattHours,
    double AverageWatts,
    double PeakWatts,
    double CoveredHours)
{
    public static PeriodStatistics Empty { get; } = new(0, 0, 0, 0);

    public bool HasData => CoveredHours > 0;
}

/// <summary>
/// Everything the statistics window shows. Produced by <see cref="StatisticsCalculator"/>.
/// </summary>
public sealed class EnergyStatistics
{
    public static EnergyStatistics Empty { get; } = new();

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UnixEpoch;

    /// <summary>Consumption of the current local calendar day.</summary>
    public PeriodStatistics Today { get; init; } = PeriodStatistics.Empty;

    /// <summary>Consumption of the current local calendar month.</summary>
    public PeriodStatistics Month { get; init; } = PeriodStatistics.Empty;

    /// <summary>Consumption over the entire recorded history.</summary>
    public PeriodStatistics AllTime { get; init; } = PeriodStatistics.Empty;

    /// <summary>
    /// Average energy per calendar day. Days on which the machine was switched off count as
    /// zero, which is what makes the annual projection realistic.
    /// </summary>
    public double AverageDailyKilowattHours { get; init; }

    /// <summary>Average power across all recorded days, including downtime.</summary>
    public double AverageWattsIncludingDowntime => AverageDailyKilowattHours * 1000.0 / 24.0;

    /// <summary>Projected consumption of the current month (actual so far plus the remaining days).</summary>
    public double ProjectedMonthKilowattHours { get; init; }

    /// <summary>Projected consumption of a full year at the observed usage pattern.</summary>
    public double ProjectedYearKilowattHours { get; init; }

    /// <summary>Highest single sample ever recorded.</summary>
    public PeakRecord? AllTimePeak { get; init; }

    /// <summary>Number of local calendar days that contain at least one sample.</summary>
    public int DaysWithData { get; init; }

    /// <summary>First day with recorded data.</summary>
    public DateOnly? FirstDay { get; init; }

    public ForecastQuality Quality { get; init; } = ForecastQuality.VeryLow;

    /// <summary>Daily history for the bar chart, oldest first, gaps filled with zero.</summary>
    public IReadOnlyList<DailyEnergy> RecentDays { get; init; } = Array.Empty<DailyEnergy>();

    /// <summary>Energy per component over the last 30 days, largest first.</summary>
    public IReadOnlyList<ComponentEnergy> ComponentBreakdown { get; init; } = Array.Empty<ComponentEnergy>();

    // ---- Cost ---------------------------------------------------------------------

    /// <summary>Electricity price used for the cost figures, in currency units per kWh.</summary>
    public decimal PricePerKilowattHour { get; init; }

    public decimal TodayCost => ToCost(Today.EnergyKilowattHours);

    public decimal MonthCost => ToCost(Month.EnergyKilowattHours);

    public decimal ProjectedMonthCost => ToCost(ProjectedMonthKilowattHours);

    public decimal ProjectedYearCost => ToCost(ProjectedYearKilowattHours);

    private decimal ToCost(double kilowattHours) =>
        Math.Round((decimal)kilowattHours * PricePerKilowattHour, 2, MidpointRounding.AwayFromZero);
}
