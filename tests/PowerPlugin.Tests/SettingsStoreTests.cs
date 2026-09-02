using PowerPlugin.Core.Configuration;
using Xunit;

namespace PowerPlugin.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"powerplugin-settings-{Guid.NewGuid():N}");

    private readonly string _file;

    public SettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "settings.json");
    }

    [Fact]
    public void MissingFileYieldsTheDefaults()
    {
        AppSettings settings = new SettingsStore(_file).Load();

        Assert.Equal(0.5, settings.SampleIntervalSeconds);
        Assert.Equal(TrayDisplayMode.TotalWatts, settings.TrayDisplay);
        Assert.Equal(1.0, settings.Model.ReportingThresholdWatts);

        // The tray refreshes twice per second and shows the mean of the last three seconds.
        Assert.Equal(TimeSpan.FromMilliseconds(500), settings.TrayRefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.TrayAverageWindow);
    }

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var store = new SettingsStore(_file);

        var written = new AppSettings
        {
            SampleIntervalSeconds = 5,
            PricePerKilowattHour = 0.4123m,
            CurrencySymbol = "CHF",
            StartWithWindows = true,
            TrayDisplay = TrayDisplayMode.TodayCost,
            HistoryRetentionDays = 90,
        };

        written.Model.PowerSupplyEfficiency = 0.92;
        written.Model.CpuTdpWattsOverride = 105;

        store.Save(written);
        AppSettings read = store.Load();

        Assert.Equal(5, read.SampleIntervalSeconds);
        Assert.Equal(0.4123m, read.PricePerKilowattHour);
        Assert.Equal("CHF", read.CurrencySymbol);
        Assert.True(read.StartWithWindows);
        Assert.Equal(TrayDisplayMode.TodayCost, read.TrayDisplay);
        Assert.Equal(90, read.HistoryRetentionDays);
        Assert.Equal(0.92, read.Model.PowerSupplyEfficiency);
        Assert.Equal(105, read.Model.CpuTdpWattsOverride);
    }

    [Fact]
    public void ADamagedFileFallsBackToDefaultsAndIsKeptAsBackup()
    {
        File.WriteAllText(_file, "{ this is not json");

        AppSettings settings = new SettingsStore(_file).Load();

        Assert.Equal(0.5, settings.SampleIntervalSeconds);
        Assert.True(File.Exists(_file + ".bak"), "Die beschädigte Datei sollte als .bak erhalten bleiben.");
    }

    [Fact]
    public void UnknownFieldsDoNotBreakLoading()
    {
        // Forward compatibility: a settings file from a newer version must still load.
        File.WriteAllText(_file, """{ "SampleIntervalSeconds": 7, "SomethingFromTheFuture": true }""");

        Assert.Equal(7, new SettingsStore(_file).Load().SampleIntervalSeconds);
    }

    [Fact]
    public void SampleIntervalIsClampedToAUsableRange()
    {
        Assert.Equal(TimeSpan.FromSeconds(0.5), new AppSettings { SampleIntervalSeconds = 0.01 }.SampleInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), new AppSettings { SampleIntervalSeconds = 9999 }.SampleInterval);
    }

    [Fact]
    public void TrayTimingIsClampedToAUsableRange()
    {
        // A refresh every few milliseconds would redraw the icon pointlessly often.
        Assert.Equal(
            TimeSpan.FromMilliseconds(100),
            new AppSettings { TrayRefreshMilliseconds = 1 }.TrayRefreshInterval);

        Assert.Equal(
            TimeSpan.FromSeconds(0.5),
            new AppSettings { TrayAverageWindowSeconds = 0 }.TrayAverageWindow);
    }

    [Fact]
    public void TrayTimingSurvivesARoundTrip()
    {
        var store = new SettingsStore(_file);
        store.Save(new AppSettings { TrayRefreshMilliseconds = 250, TrayAverageWindowSeconds = 8 });

        AppSettings read = store.Load();

        Assert.Equal(250, read.TrayRefreshMilliseconds);
        Assert.Equal(8, read.TrayAverageWindowSeconds);
    }

    [Fact]
    public void CloningDoesNotShareTheModel()
    {
        var original = new AppSettings();
        AppSettings copy = original.Clone();

        copy.Model.DesktopBaseWatts = 999;

        Assert.NotEqual(999, original.Model.DesktopBaseWatts);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
