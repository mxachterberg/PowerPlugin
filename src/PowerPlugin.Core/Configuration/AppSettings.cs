using PowerPlugin.Core.Estimation;

namespace PowerPlugin.Core.Configuration;

/// <summary>What the tray icon shows.</summary>
public enum TrayDisplayMode
{
    /// <summary>The total system power in watts.</summary>
    TotalWatts,

    /// <summary>Today's energy in kWh.</summary>
    TodayKilowattHours,

    /// <summary>Today's cost.</summary>
    TodayCost,
}

/// <summary>How the power value shown in the notification area is derived from the samples.</summary>
public enum TrayValueMode
{
    /// <summary>The latest sample, unsmoothed. Follows load changes immediately but jumps around.</summary>
    Instantaneous,

    /// <summary>The mean over <see cref="AppSettings.TrayAverageWindowSeconds"/>.</summary>
    Average,
}

/// <summary>
/// User configuration, persisted as JSON.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Time between two sensor readings. Half a second keeps up with the refresh rate of the
    /// tray icon; raising it lowers the program's own load at the cost of a lazier display.
    /// </summary>
    public double SampleIntervalSeconds { get; set; } = 0.5;

    /// <summary>Electricity price per kWh in the currency below.</summary>
    public decimal PricePerKilowattHour { get; set; } = 0.35m;

    public string CurrencySymbol { get; set; } = "€";

    public bool StartWithWindows { get; set; }

    /// <summary>Start without opening the statistics window - the usual mode for an autostart entry.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Close the window to the tray instead of quitting the program.</summary>
    public bool CloseToTray { get; set; } = true;

    public TrayDisplayMode TrayDisplay { get; set; } = TrayDisplayMode.TotalWatts;

    /// <summary>
    /// How often the value in the notification area is recalculated. Independent of both the
    /// sampling rate and the averaging window, so "every five seconds the mean of the last five"
    /// and "every second the mean of the last five" are both possible.
    /// </summary>
    public double TrayRefreshSeconds { get; set; } = 0.5;

    /// <summary>Whether the tray shows the latest sample or a mean over a window.</summary>
    public TrayValueMode TrayValue { get; set; } = TrayValueMode.Average;

    /// <summary>
    /// Length of the averaging window, used when <see cref="TrayValue"/> is
    /// <see cref="TrayValueMode.Average"/>. Without smoothing the number jumps by tens of watts
    /// between two samples and is hard to read at a fast refresh rate.
    /// </summary>
    public double TrayAverageWindowSeconds { get; set; } = 3.0;

    /// <summary>Below this the tray icon is green.</summary>
    public double TrayGreenThresholdWatts { get; set; } = 80;

    /// <summary>Below this the tray icon is amber, above it turns red.</summary>
    public double TrayAmberThresholdWatts { get; set; } = 200;

    /// <summary>History older than this is deleted on startup. Set to 0 to keep everything.</summary>
    public int HistoryRetentionDays { get; set; } = 400;

    /// <summary>All coefficients of the estimation model, exposed so the model can be calibrated.</summary>
    public PowerModelOptions Model { get; set; } = new();

    public TimeSpan SampleInterval =>
        TimeSpan.FromSeconds(Math.Clamp(SampleIntervalSeconds, 0.5, 60.0));

    public TimeSpan TrayRefreshInterval =>
        TimeSpan.FromSeconds(Math.Clamp(TrayRefreshSeconds, 0.1, 60.0));

    public TimeSpan TrayAverageWindow =>
        TimeSpan.FromSeconds(Math.Clamp(TrayAverageWindowSeconds, 0.5, 600.0));

    /// <summary>
    /// The window the tray actually averages over. Zero in instantaneous mode, which makes the
    /// averaging collapse to the latest sample.
    /// </summary>
    public TimeSpan EffectiveTrayAverageWindow =>
        TrayValue == TrayValueMode.Average ? TrayAverageWindow : TimeSpan.Zero;

    public AppSettings Clone() => new()
    {
        SampleIntervalSeconds = SampleIntervalSeconds,
        TrayRefreshSeconds = TrayRefreshSeconds,
        TrayValue = TrayValue,
        TrayAverageWindowSeconds = TrayAverageWindowSeconds,
        PricePerKilowattHour = PricePerKilowattHour,
        CurrencySymbol = CurrencySymbol,
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        CloseToTray = CloseToTray,
        TrayDisplay = TrayDisplay,
        TrayGreenThresholdWatts = TrayGreenThresholdWatts,
        TrayAmberThresholdWatts = TrayAmberThresholdWatts,
        HistoryRetentionDays = HistoryRetentionDays,
        Model = Model.Clone(),
    };
}
