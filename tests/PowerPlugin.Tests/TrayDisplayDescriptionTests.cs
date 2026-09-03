using System.Globalization;
using PowerPlugin.Core.Configuration;
using Xunit;

namespace PowerPlugin.Tests;

/// <summary>
/// The settings page reads the configuration back as a sentence. These tests pin down that the
/// three combinations the user asked for are described correctly, and that the two misleading
/// combinations are called out.
/// </summary>
public sealed class TrayDisplayDescriptionTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    public TrayDisplayDescriptionTests() =>
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void EveryFiveSecondsTheMeanOfTheLastFive()
    {
        var settings = new AppSettings
        {
            SampleIntervalSeconds = 0.5,
            TrayRefreshSeconds = 5,
            TrayValue = TrayValueMode.Average,
            TrayAverageWindowSeconds = 5,
        };

        string text = TrayDisplayDescription.Describe(settings);

        Assert.Contains("Alle 5 s", text, StringComparison.Ordinal);
        Assert.Contains("Mittelwert der letzten 5 s", text, StringComparison.Ordinal);
        Assert.Contains("10 Messwerte", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryThreeSecondsASingleSnapshot()
    {
        var settings = new AppSettings
        {
            SampleIntervalSeconds = 0.5,
            TrayRefreshSeconds = 3,
            TrayValue = TrayValueMode.Instantaneous,
        };

        string text = TrayDisplayDescription.Describe(settings);

        Assert.Contains("Alle 3 s", text, StringComparison.Ordinal);
        Assert.Contains("Momentanwert", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mittelwert", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySecondTheMeanOfTheLastFive()
    {
        var settings = new AppSettings
        {
            SampleIntervalSeconds = 0.5,
            TrayRefreshSeconds = 1,
            TrayValue = TrayValueMode.Average,
            TrayAverageWindowSeconds = 5,
        };

        string text = TrayDisplayDescription.Describe(settings);

        Assert.Contains("Alle 1 s", text, StringComparison.Ordinal);
        Assert.Contains("Mittelwert der letzten 5 s", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshingFasterThanMeasuringIsPointedOut()
    {
        var settings = new AppSettings
        {
            SampleIntervalSeconds = 5,
            TrayRefreshSeconds = 0.5,
            TrayValue = TrayValueMode.Instantaneous,
        };

        string text = TrayDisplayDescription.Describe(settings);

        Assert.Contains("wiederholt die Anzeige denselben Wert", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowShorterThanTheSamplingIntervalIsPointedOut()
    {
        // Averaging over one second while measuring once per second smooths nothing.
        var settings = new AppSettings
        {
            SampleIntervalSeconds = 1,
            TrayRefreshSeconds = 1,
            TrayValue = TrayValueMode.Average,
            TrayAverageWindowSeconds = 1,
        };

        string text = TrayDisplayDescription.Describe(settings);

        Assert.Contains("faktisch ungeglättet", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASensibleCombinationCarriesNoWarning()
    {
        var settings = new AppSettings
        {
            SampleIntervalSeconds = 0.5,
            TrayRefreshSeconds = 0.5,
            TrayValue = TrayValueMode.Average,
            TrayAverageWindowSeconds = 3,
        };

        string text = TrayDisplayDescription.Describe(settings);

        Assert.DoesNotContain("ungeglättet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("wiederholt", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TrayDisplayMode.TodayKilowattHours, "Verbrauch in kWh")]
    [InlineData(TrayDisplayMode.TodayCost, "Stromkosten")]
    public void EnergyAndCostModesAreUnaffectedBySmoothing(TrayDisplayMode mode, string expected)
    {
        var settings = new AppSettings { TrayDisplay = mode, TrayValue = TrayValueMode.Average };

        string text = TrayDisplayDescription.Describe(settings);

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains("wirken sich auf diese Anzeige nicht aus", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.5, "500 ms")]
    [InlineData(1, "1 s")]
    [InlineData(2.5, "2,5 s")]
    [InlineData(10, "10 s")]
    public void DurationsReadTheWayTheyAreSpoken(double seconds, string expected) =>
        Assert.Equal(expected, TrayDisplayDescription.FormatDuration(TimeSpan.FromSeconds(seconds)));

    public void Dispose() => CultureInfo.CurrentCulture = _originalCulture;
}
