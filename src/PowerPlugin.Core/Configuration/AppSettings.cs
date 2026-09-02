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

/// <summary>
/// User configuration, persisted as JSON.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Time between two sensor readings. Two seconds keeps the tray value lively without load.</summary>
    public double SampleIntervalSeconds { get; set; } = 2.0;

    /// <summary>Electricity price per kWh in the currency below.</summary>
    public decimal PricePerKilowattHour { get; set; } = 0.35m;

    public string CurrencySymbol { get; set; } = "€";

    public bool StartWithWindows { get; set; }

    /// <summary>Start without opening the statistics window - the usual mode for an autostart entry.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Close the window to the tray instead of quitting the program.</summary>
    public bool CloseToTray { get; set; } = true;

    public TrayDisplayMode TrayDisplay { get; set; } = TrayDisplayMode.TotalWatts;

    /// <summary>Below this the tray icon is green.</summary>
    public double TrayGreenThresholdWatts { get; set; } = 80;

    /// <summary>Below this the tray icon is amber, above it turns red.</summary>
    public double TrayAmberThresholdWatts { get; set; } = 200;

    /// <summary>History older than this is deleted on startup. Set to 0 to keep everything.</summary>
    public int HistoryRetentionDays { get; set; } = 400;

    /// <summary>All coefficients of the estimation model, exposed so the model can be calibrated.</summary>
    public PowerModelOptions Model { get; set; } = new();

    public TimeSpan SampleInterval =>
        TimeSpan.FromSeconds(Math.Clamp(SampleIntervalSeconds, 1.0, 60.0));

    public AppSettings Clone() => new()
    {
        SampleIntervalSeconds = SampleIntervalSeconds,
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
