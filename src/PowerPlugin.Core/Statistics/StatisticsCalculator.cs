using PowerPlugin.Core.Storage;

namespace PowerPlugin.Core.Statistics;

/// <summary>
/// Derives the consumption statistics from the stored history.
/// <para>
/// Two different kinds of average are reported on purpose:
/// <list type="bullet">
///   <item><description>
///     <c>AverageWatts</c> of a period is the average <i>while the machine was running</i>.
///     It answers "how much does this PC pull when I use it".
///   </description></item>
///   <item><description>
///     <c>AverageDailyKilowattHours</c> divides the recorded energy by all calendar days since
///     the first recording, including the days the machine stayed off. It answers "what does
///     this PC cost me per day" and is therefore the basis for the monthly and annual forecast.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class StatisticsCalculator(IEnergyStore store)
{
    /// <summary>Number of days shown in the history chart.</summary>
    public const int HistoryWindowDays = 30;

    private const double DaysPerYear = 365.25;

    private readonly IEnergyStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public EnergyStatistics Calculate(DateTimeOffset nowLocal, decimal pricePerKilowattHour)
    {
        DateOnly today = DateOnly.FromDateTime(nowLocal.DateTime);
        StoreTotals totals = _store.GetTotals();

        if (totals.FirstDay is not { } firstDay)
        {
            return new EnergyStatistics
            {
                GeneratedAt = nowLocal,
                PricePerKilowattHour = pricePerKilowattHour,
                RecentDays = BuildDenseHistory(Array.Empty<DailyEnergy>(), today.AddDays(-(HistoryWindowDays - 1)), today),
            };
        }

        DateOnly historyStart = today.AddDays(-(HistoryWindowDays - 1));
        IReadOnlyList<DailyEnergy> recentDays = BuildDenseHistory(
            _store.GetDailyEnergy(historyStart, today), historyStart, today);

        PeriodStatistics todayStats = ToPeriod(_store.GetDailyEnergy(today, today));

        DateOnly monthStart = new(today.Year, today.Month, 1);
        IReadOnlyList<DailyEnergy> monthDays = _store.GetDailyEnergy(monthStart, today);
        PeriodStatistics monthStats = ToPeriod(monthDays);

        PeakRecord? allTimePeak = _store.GetAllTimePeak();
        var allTimeStats = new PeriodStatistics(
            totals.EnergyWattHours / 1000.0,
            totals.CoveredSeconds > 0 ? totals.EnergyWattHours * 3600.0 / totals.CoveredSeconds : 0,
            allTimePeak?.Watts ?? 0,
            totals.CoveredSeconds / 3600.0);

        double averageDailyKwh = CalculateAverageDailyKilowattHours(totals, todayStats, firstDay, today);

        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        double remainingDays = Math.Max(0, daysInMonth - today.Day);
        double projectedMonth = monthStats.EnergyKilowattHours + (averageDailyKwh * remainingDays);

        return new EnergyStatistics
        {
            GeneratedAt = nowLocal,
            Today = todayStats,
            Month = monthStats,
            AllTime = allTimeStats,
            AverageDailyKilowattHours = averageDailyKwh,
            ProjectedMonthKilowattHours = projectedMonth,
            ProjectedYearKilowattHours = averageDailyKwh * DaysPerYear,
            AllTimePeak = allTimePeak,
            DaysWithData = totals.DaysWithData,
            FirstDay = firstDay,
            Quality = DetermineQuality(firstDay, today, totals),
            RecentDays = recentDays,
            ComponentBreakdown = _store.GetComponentEnergy(
                nowLocal.ToUniversalTime().AddDays(-HistoryWindowDays),
                nowLocal.ToUniversalTime()),
            PricePerKilowattHour = pricePerKilowattHour,
        };
    }

    /// <summary>
    /// Average energy per calendar day.
    /// <para>
    /// Once at least one complete day has been recorded, the sum of all completed days is
    /// divided by the number of calendar days that have elapsed since the first recording.
    /// Days without any data count as zero, because the machine was switched off - excluding
    /// them would systematically overestimate the annual consumption.
    /// </para>
    /// <para>
    /// Before the first day is complete there is nothing to average, so the day so far is
    /// extrapolated to 24 hours. That is flagged as <see cref="ForecastQuality.VeryLow"/>.
    /// </para>
    /// </summary>
    private double CalculateAverageDailyKilowattHours(
        StoreTotals totals,
        PeriodStatistics todayStats,
        DateOnly firstDay,
        DateOnly today)
    {
        int completedDays = today.DayNumber - firstDay.DayNumber;

        if (completedDays >= 1)
        {
            double completedEnergyKwh = (totals.EnergyWattHours / 1000.0) - todayStats.EnergyKilowattHours;
            return Math.Max(0, completedEnergyKwh) / completedDays;
        }

        // First day: extrapolate the observed average power over a full 24 hours.
        if (todayStats.CoveredHours <= 0)
        {
            return 0;
        }

        return todayStats.AverageWatts * 24.0 / 1000.0;
    }

    private static ForecastQuality DetermineQuality(DateOnly firstDay, DateOnly today, StoreTotals totals)
    {
        int span = today.DayNumber - firstDay.DayNumber;

        if (span >= 30 && totals.DaysWithData >= 7)
        {
            return ForecastQuality.High;
        }

        if (span >= 7)
        {
            return ForecastQuality.Medium;
        }

        return span >= 1 ? ForecastQuality.Low : ForecastQuality.VeryLow;
    }

    private static PeriodStatistics ToPeriod(IReadOnlyList<DailyEnergy> days)
    {
        if (days.Count == 0)
        {
            return PeriodStatistics.Empty;
        }

        double energyWh = days.Sum(d => d.EnergyWattHours);
        double coveredSeconds = days.Sum(d => d.CoveredSeconds);

        return new PeriodStatistics(
            energyWh / 1000.0,
            coveredSeconds > 0 ? energyWh * 3600.0 / coveredSeconds : 0,
            days.Max(d => d.PeakWatts),
            coveredSeconds / 3600.0);
    }

    /// <summary>
    /// Fills days without recordings with zero so the chart keeps a continuous time axis.
    /// </summary>
    private static IReadOnlyList<DailyEnergy> BuildDenseHistory(
        IReadOnlyList<DailyEnergy> sparse,
        DateOnly from,
        DateOnly to)
    {
        Dictionary<DateOnly, DailyEnergy> byDay = sparse.ToDictionary(d => d.Day);
        var result = new List<DailyEnergy>(Math.Max(1, to.DayNumber - from.DayNumber + 1));

        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            result.Add(byDay.TryGetValue(day, out DailyEnergy? entry) ? entry : DailyEnergy.Zero(day));
        }

        return result;
    }
}
