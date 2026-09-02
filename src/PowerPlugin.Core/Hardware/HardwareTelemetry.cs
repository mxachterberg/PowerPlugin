namespace PowerPlugin.Core.Hardware;

/// <summary>
/// One reading of a hardware power sensor.
/// </summary>
/// <param name="Key">Matches the key of the inventory item the sensor belongs to.</param>
/// <param name="Watts">Measured power.</param>
/// <param name="SensorName">Name of the sensor as reported by the driver, for diagnostics.</param>
public sealed record PowerSensorReading(string Key, double Watts, string SensorName);

/// <summary>Utilisation of a single device, expressed as a fraction between 0 and 1.</summary>
public sealed record LoadReading(string Key, double Load);

/// <summary>
/// The volatile part of the hardware state, refreshed on every sampling tick.
/// All loads are fractions in the range 0..1.
/// </summary>
public sealed record HardwareTelemetry
{
    public static HardwareTelemetry Empty { get; } = new();

    /// <summary>Total CPU utilisation.</summary>
    public double CpuLoad { get; init; }

    /// <summary>Power sensor for the CPU package, if the platform exposes one.</summary>
    public double? CpuPackageWatts { get; init; }

    /// <summary>Per GPU load, keyed by <see cref="GpuInfo.Key"/>.</summary>
    public IReadOnlyDictionary<string, double> GpuLoads { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per GPU board power sensors, keyed by <see cref="GpuInfo.Key"/>.</summary>
    public IReadOnlyDictionary<string, double> GpuWatts { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fraction of installed memory currently in use.</summary>
    public double MemoryLoad { get; init; }

    /// <summary>Per drive activity, keyed by <see cref="StorageDeviceInfo.Key"/>.</summary>
    public IReadOnlyDictionary<string, double> StorageActivity { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fan speeds as a fraction of the highest speed seen so far.</summary>
    public IReadOnlyList<double> FanLoads { get; init; } = Array.Empty<double>();

    /// <summary>Additional power sensors that do not map to a known inventory item (PSU rails, chipset, ...).</summary>
    public IReadOnlyList<PowerSensorReading> AdditionalPowerSensors { get; init; } =
        Array.Empty<PowerSensorReading>();

    /// <summary>Total DC power reported by a PSU with telemetry support, if present.</summary>
    public double? PsuOutputWatts { get; init; }

    /// <summary>Positive discharge rate in watts while the machine runs on battery.</summary>
    public double? BatteryDischargeWatts { get; init; }

    /// <summary>True while the machine runs on mains power.</summary>
    public bool IsOnAcPower { get; init; } = true;

    /// <summary>Display brightness as a fraction, used to model the internal panel of a notebook.</summary>
    public double? DisplayBrightness { get; init; }

    /// <summary>Set when the sensor backend is running without the privileges it needs.</summary>
    public bool RequiresElevation { get; init; }
}
