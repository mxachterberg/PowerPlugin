using PowerPlugin.Core.Statistics;
using PowerPlugin.Core.Storage;
using Xunit;

namespace PowerPlugin.Tests;

public sealed class StatisticsCalculatorTests
{
    private const decimal Price = 0.30m;

    private static readonly DateTimeOffset Now = new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 3, 14);

    private readonly FakeEnergyStore _store = new();
    private readonly StatisticsCalculator _calculator;

    public StatisticsCalculatorTests() => _calculator = new StatisticsCalculator(_store);

    /// <summary>Records one day that consumed <paramref name="watts"/> for <paramref name="hours"/>.</summary>
    private void AddDay(DateOnly day, double watts, double hours, double? peakWatts = null)
    {
        double seconds = hours * 3600;

        _store.UpsertBucket(new EnergyBucket(
            new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            DayKey.Encode(day),
            watts * seconds / 3600.0,
            seconds,
            peakWatts ?? watts,
            watts,
            (int)Math.Max(1, seconds / 2)));
    }

    [Fact]
    public void EmptyHistoryProducesZeroesAndAFullChartAxis()
    {
        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        Assert.Equal(0, statistics.Today.EnergyKilowattHours);
        Assert.Equal(0, statistics.ProjectedYearKilowattHours);
        Assert.Equal(ForecastQuality.VeryLow, statistics.Quality);
        Assert.Null(statistics.FirstDay);

        // The chart axis must still span the full window so it does not appear broken.
        Assert.Equal(StatisticsCalculator.HistoryWindowDays, statistics.RecentDays.Count);
        Assert.Equal(Today, statistics.RecentDays[^1].Day);
    }

    [Fact]
    public void TodaysAverageIsTheAverageWhileRunning()
    {
        AddDay(Today, watts: 150, hours: 4);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        Assert.Equal(150, statistics.Today.AverageWatts, precision: 6);
        Assert.Equal(0.6, statistics.Today.EnergyKilowattHours, precision: 6);
        Assert.Equal(4, statistics.Today.CoveredHours, precision: 6);
    }

    [Fact]
    public void PeakIsReportedForTodayAndForTheWholeHistory()
    {
        AddDay(Today.AddDays(-3), watts: 100, hours: 5, peakWatts: 480);
        AddDay(Today, watts: 120, hours: 2, peakWatts: 260);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        Assert.Equal(260, statistics.Today.PeakWatts);
        Assert.NotNull(statistics.AllTimePeak);
        Assert.Equal(480, statistics.AllTimePeak!.Watts);
    }

    [Fact]
    public void MonthlyFiguresCoverTheCurrentCalendarMonthOnly()
    {
        AddDay(new DateOnly(2026, 2, 28), watts: 200, hours: 10);  // previous month
        AddDay(new DateOnly(2026, 3, 1), watts: 100, hours: 10);
        AddDay(Today, watts: 100, hours: 5);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        // 1.0 kWh on the first plus 0.5 kWh today, February must not leak in.
        Assert.Equal(1.5, statistics.Month.EnergyKilowattHours, precision: 6);
        Assert.Equal(100, statistics.Month.AverageWatts, precision: 6);
    }

    [Fact]
    public void DaysWithoutDataCountAsZeroInTheDailyAverage()
    {
        // One full day 5 days ago, then the machine stayed off.
        AddDay(Today.AddDays(-5), watts: 100, hours: 10);   // 1 kWh
        AddDay(Today, watts: 100, hours: 1);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        // 1 kWh spread over the five completed days, not over the single day with data.
        Assert.Equal(0.2, statistics.AverageDailyKilowattHours, precision: 6);
    }

    [Fact]
    public void AnnualForecastExtrapolatesTheDailyAverage()
    {
        AddDay(Today.AddDays(-1), watts: 250, hours: 8);    // 2 kWh yesterday
        AddDay(Today, watts: 250, hours: 1);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        Assert.Equal(2.0, statistics.AverageDailyKilowattHours, precision: 6);
        Assert.Equal(2.0 * 365.25, statistics.ProjectedYearKilowattHours, precision: 6);
        Assert.Equal(Math.Round(2.0m * 365.25m * Price, 2), statistics.ProjectedYearCost);
    }

    [Fact]
    public void OnTheFirstDayTheForecastExtrapolatesToTwentyFourHours()
    {
        AddDay(Today, watts: 100, hours: 6);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        // No completed day exists yet, so 100 W are projected across a full day.
        Assert.Equal(2.4, statistics.AverageDailyKilowattHours, precision: 6);
        Assert.Equal(ForecastQuality.VeryLow, statistics.Quality);
    }

    [Fact]
    public void MonthlyProjectionAddsTheRemainingDays()
    {
        AddDay(Today.AddDays(-1), watts: 250, hours: 8);    // 2 kWh
        AddDay(Today, watts: 250, hours: 4);                // 1 kWh

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        // March has 31 days, 17 of them are still ahead on the 14th.
        double expected = statistics.Month.EnergyKilowattHours + (statistics.AverageDailyKilowattHours * 17);
        Assert.Equal(expected, statistics.ProjectedMonthKilowattHours, precision: 6);
    }

    [Theory]
    [InlineData(0, ForecastQuality.VeryLow)]
    [InlineData(3, ForecastQuality.Low)]
    [InlineData(10, ForecastQuality.Medium)]
    public void ForecastQualityGrowsWithTheLengthOfTheHistory(int daysBack, ForecastQuality expected)
    {
        AddDay(Today.AddDays(-daysBack), watts: 100, hours: 5);

        if (daysBack > 0)
        {
            AddDay(Today, watts: 100, hours: 1);
        }

        Assert.Equal(expected, _calculator.Calculate(Now, Price).Quality);
    }

    [Fact]
    public void ALongHistoryReachesTheHighestForecastQuality()
    {
        for (int i = 0; i <= 40; i += 5)
        {
            AddDay(Today.AddDays(-i), watts: 100, hours: 5);
        }

        Assert.Equal(ForecastQuality.High, _calculator.Calculate(Now, Price).Quality);
    }

    [Fact]
    public void HistoryChartFillsMissingDaysWithZero()
    {
        AddDay(Today.AddDays(-2), watts: 100, hours: 5);
        AddDay(Today, watts: 100, hours: 5);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        Assert.Equal(StatisticsCalculator.HistoryWindowDays, statistics.RecentDays.Count);

        // Days are contiguous and in ascending order.
        for (int i = 1; i < statistics.RecentDays.Count; i++)
        {
            Assert.Equal(statistics.RecentDays[i - 1].Day.AddDays(1), statistics.RecentDays[i].Day);
        }

        DailyEnergy yesterday = statistics.RecentDays.Single(d => d.Day == Today.AddDays(-1));
        Assert.Equal(0, yesterday.EnergyWattHours);
    }

    [Fact]
    public void CostsFollowTheConfiguredPrice()
    {
        AddDay(Today, watts: 1000, hours: 1);   // exactly 1 kWh

        EnergyStatistics statistics = _calculator.Calculate(Now, 0.42m);

        Assert.Equal(0.42m, statistics.TodayCost);
        Assert.Equal(0.42m, statistics.MonthCost);
    }

    [Fact]
    public void AverageIncludingDowntimeIsDerivedFromTheDailyEnergy()
    {
        AddDay(Today.AddDays(-1), watts: 240, hours: 10);   // 2.4 kWh
        AddDay(Today, watts: 240, hours: 1);

        EnergyStatistics statistics = _calculator.Calculate(Now, Price);

        // 2.4 kWh per day equals a round-the-clock average of 100 W.
        Assert.Equal(100, statistics.AverageWattsIncludingDowntime, precision: 6);
    }
}
