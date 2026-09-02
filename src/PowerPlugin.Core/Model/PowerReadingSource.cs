namespace PowerPlugin.Core.Model;

/// <summary>
/// Describes how a wattage value was obtained. The UI surfaces this so the user can
/// tell a hardware measurement apart from a model based approximation.
/// </summary>
public enum PowerReadingSource
{
    /// <summary>Read from a dedicated power sensor (RAPL, SMU, NVML, PSU telemetry).</summary>
    Sensor,

    /// <summary>Derived from the battery discharge rate, i.e. a real measurement of the whole system.</summary>
    BatteryMeasurement,

    /// <summary>Derived from load/utilisation telemetry through the estimation model.</summary>
    Estimated,

    /// <summary>A static or semi-static model value (chassis base load, conversion losses).</summary>
    Modeled,
}

public static class PowerReadingSourceExtensions
{
    public static string ToDisplayString(this PowerReadingSource source) => source switch
    {
        PowerReadingSource.Sensor => "Sensor",
        PowerReadingSource.BatteryMeasurement => "Akku-Messung",
        PowerReadingSource.Estimated => "Geschätzt",
        PowerReadingSource.Modeled => "Modell",
        _ => "Unbekannt",
    };

    /// <summary>True when the value comes from real hardware telemetry rather than a model.</summary>
    public static bool IsMeasured(this PowerReadingSource source) =>
        source is PowerReadingSource.Sensor or PowerReadingSource.BatteryMeasurement;
}
