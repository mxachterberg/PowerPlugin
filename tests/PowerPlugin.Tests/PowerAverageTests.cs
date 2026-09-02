using System.Collections.Immutable;
using PowerPlugin.Core.Model;
using PowerPlugin.Core.Monitoring;
using Xunit;

namespace PowerPlugin.Tests;

public sealed class PowerAverageTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private static PowerSnapshot At(double seconds, double watts) => new(
        Start.AddSeconds(seconds),
        [new PowerComponent("cpu", "CPU", ComponentCategory.Cpu, watts, PowerReadingSource.Estimated)],
        belowThresholdWatts: 0,
        conversionLossWatts: 0,
        measuredSystemWatts: null,
        confidence: MeasurementConfidence.Low);

    private static double Average(IEnumerable<PowerSnapshot> samples, double nowSeconds, double fallback = 0) =>
        PowerAverage.TimeWeightedWatts(samples, Start.AddSeconds(nowSeconds), Window, fallback);

    [Fact]
    public void NoSamplesYieldsTheFallback()
    {
        Assert.Equal(42, Average([], nowSeconds: 5, fallback: 42));
    }

    [Fact]
    public void ASingleSampleIsHeldForTheWholeWindow()
    {
        // The newest sample must carry weight even though nothing follows it yet.
        Assert.Equal(80, Average([At(0, 80)], nowSeconds: 2), precision: 6);
    }

    [Fact]
    public void ConstantPowerAveragesToItself()
    {
        PowerSnapshot[] samples = [At(0, 120), At(1, 120), At(2, 120)];

        Assert.Equal(120, Average(samples, nowSeconds: 3), precision: 6);
    }

    [Fact]
    public void EachSampleIsWeightedByTheTimeItCovers()
    {
        // 100 W for one second, then 200 W for two seconds.
        PowerSnapshot[] samples = [At(0, 100), At(1, 200)];

        Assert.Equal(((100 * 1) + (200 * 2)) / 3.0, Average(samples, nowSeconds: 3), precision: 6);
    }

    [Fact]
    public void CountingSamplesInsteadOfTimeWouldGiveTheWrongAnswer()
    {
        // Three quick samples at 300 W, then one at 100 W that covers most of the window.
        PowerSnapshot[] samples = [At(0.0, 300), At(0.1, 300), At(0.2, 300), At(0.3, 100)];

        double average = Average(samples, nowSeconds: 3);

        // The unweighted mean would be 250 W; the correct, time weighted answer is close to 100 W.
        Assert.InRange(average, 100, 130);
    }

    [Fact]
    public void SamplesOlderThanTheWindowAreIgnored()
    {
        // The 500 W spike is nine seconds old and must not show up in a three second window.
        PowerSnapshot[] samples = [At(0, 500), At(9, 100)];

        Assert.Equal(100, Average(samples, nowSeconds: 12), precision: 6);
    }

    [Fact]
    public void ASampleReachingIntoTheWindowIsCountedProRata()
    {
        // 400 W held from second 0 until second 8, then 100 W. Window is [7, 10).
        PowerSnapshot[] samples = [At(0, 400), At(8, 100)];

        Assert.Equal(((400 * 1) + (100 * 2)) / 3.0, Average(samples, nowSeconds: 10), precision: 6);
    }

    [Fact]
    public void StaleHistoryFallsBackToTheLastKnownValue()
    {
        // Sampling stopped long ago: the newest value is held rather than reported as zero.
        Assert.Equal(75, Average([At(0, 75)], nowSeconds: 600), precision: 6);
    }

    [Fact]
    public void AZeroWindowReturnsTheFallback()
    {
        double result = PowerAverage.TimeWeightedWatts(
            [At(0, 90)], Start.AddSeconds(1), TimeSpan.Zero, fallbackWatts: 7);

        Assert.Equal(7, result);
    }

    [Fact]
    public void AClockGoingBackwardsDoesNotProduceNonsense()
    {
        // "now" lies before the samples; the result must stay finite and non-negative.
        double result = Average([At(10, 130)], nowSeconds: 0);

        Assert.True(double.IsFinite(result));
        Assert.Equal(130, result, precision: 6);
    }

    [Fact]
    public void SmoothingDampensASpikeInsteadOfShowingIt()
    {
        // Half a second of 400 W inside an otherwise quiet window.
        var samples = ImmutableArray.Create(At(0, 60), At(2.5, 400));

        double average = Average(samples, nowSeconds: 3);
        double instantaneous = samples[^1].TotalWatts;

        Assert.True(average < instantaneous / 2, "Ein kurzer Ausschlag darf die Anzeige nicht dominieren.");
        Assert.True(average > 60, "Der Ausschlag muss sich aber bemerkbar machen.");
    }
}
