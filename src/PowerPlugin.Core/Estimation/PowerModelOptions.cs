using PowerPlugin.Core.Hardware;

namespace PowerPlugin.Core.Estimation;

/// <summary>
/// Every coefficient the estimation model uses. The values are persisted with the
/// settings so a user can calibrate the model against a real wall socket meter.
/// The defaults are derived from typical desktop and notebook hardware.
/// </summary>
public sealed class PowerModelOptions
{
    /// <summary>Consumers below this draw are summed up instead of listed individually.</summary>
    public double ReportingThresholdWatts { get; set; } = 1.0;

    // ---- CPU -------------------------------------------------------------------

    /// <summary>Idle package power of a desktop CPU in watts.</summary>
    public double DesktopCpuIdleWatts { get; set; } = 12.0;

    /// <summary>Idle package power of a notebook CPU in watts.</summary>
    public double MobileCpuIdleWatts { get; set; } = 3.0;

    /// <summary>
    /// Overrides the automatically derived CPU TDP. Set this to the value from the
    /// data sheet for the most accurate results on systems without a RAPL sensor.
    /// </summary>
    public double? CpuTdpWattsOverride { get; set; }

    /// <summary>Exponent of the load-to-power curve. Above 1 because power scales super-linearly with load.</summary>
    public double CpuLoadExponent { get; set; } = 1.4;

    /// <summary>
    /// Efficiency of the mainboard voltage regulators. Package sensors report the power
    /// behind the VRM, so the draw at the PSU is higher by this factor.
    /// </summary>
    public double CpuVrmEfficiency { get; set; } = 0.90;

    // ---- GPU -------------------------------------------------------------------

    public double DesktopGpuIdleWatts { get; set; } = 15.0;

    public double MobileGpuIdleWatts { get; set; } = 5.0;

    public double DesktopGpuTdpWatts { get; set; } = 180.0;

    public double MobileGpuTdpWatts { get; set; } = 60.0;

    public double GpuLoadExponent { get; set; } = 1.25;

    /// <summary>
    /// An integrated GPU shares the package power budget with the CPU. When a CPU package
    /// sensor is available the iGPU is already included and must not be counted twice.
    /// </summary>
    public double IntegratedGpuWattsWhenNoPackageSensor { get; set; } = 8.0;

    // ---- Memory ----------------------------------------------------------------

    /// <summary>Static power per module (PMIC, register, refresh) in watts.</summary>
    public double MemoryModuleBaseWatts { get; set; } = 1.2;

    /// <summary>Additional power per gigabyte of a module in watts.</summary>
    public double MemoryWattsPerGigabyte { get; set; } = 0.09;

    /// <summary>DDR5 carries its own voltage regulator on the module, which costs extra.</summary>
    public double Ddr5ModuleSurchargeWatts { get; set; } = 0.6;

    /// <summary>Share of the memory power that is already drawn while completely idle.</summary>
    public double MemoryIdleShare { get; set; } = 0.75;

    // ---- Storage ---------------------------------------------------------------

    public double NvmeIdleWatts { get; set; } = 0.9;
    public double NvmeActiveWatts { get; set; } = 5.5;
    public double SataSsdIdleWatts { get; set; } = 0.35;
    public double SataSsdActiveWatts { get; set; } = 2.6;
    public double HardDiskIdleWatts { get; set; } = 4.2;
    public double HardDiskActiveWatts { get; set; } = 7.5;

    /// <summary>Assumed drive activity when no activity sensor is available.</summary>
    public double DefaultStorageActivity { get; set; } = 0.05;

    // ---- Mainboard and cooling -------------------------------------------------

    /// <summary>Chipset, VRM idle losses, USB, audio and network of a desktop board.</summary>
    public double DesktopBaseWatts { get; set; } = 22.0;

    /// <summary>Same for a notebook mainboard.</summary>
    public double MobileBaseWatts { get; set; } = 6.0;

    public double WattsPerFanAtFullSpeed { get; set; } = 2.4;

    /// <summary>Fans are never fully off, so the model keeps a floor.</summary>
    public double FanMinimumLoad { get; set; } = 0.25;

    // ---- Display ---------------------------------------------------------------

    /// <summary>Backlight-off power of the internal notebook panel.</summary>
    public double MobileDisplayBaseWatts { get; set; } = 2.0;

    /// <summary>Additional power of the internal panel at full brightness.</summary>
    public double MobileDisplayBacklightWatts { get; set; } = 6.0;

    // ---- Conversion ------------------------------------------------------------

    /// <summary>Efficiency of the ATX power supply, e.g. 0.9 for an 80 Plus Gold unit at typical load.</summary>
    public double PowerSupplyEfficiency { get; set; } = 0.88;

    /// <summary>Efficiency of an external notebook power brick.</summary>
    public double AcAdapterEfficiency { get; set; } = 0.90;

    /// <summary>
    /// When true, the conversion losses of the PSU are added as a separate line item so the
    /// reported total matches what a wall socket meter shows.
    /// </summary>
    public bool IncludeConversionLosses { get; set; } = true;

    public double EfficiencyFor(bool isMobileSystem) =>
        Clamp(isMobileSystem ? AcAdapterEfficiency : PowerSupplyEfficiency, 0.5, 1.0);

    public double BaseWattsFor(bool isMobileSystem) =>
        Math.Max(0, isMobileSystem ? MobileBaseWatts : DesktopBaseWatts);

    /// <summary>
    /// Derives a plausible CPU TDP when the data sheet value is unknown. Desktop parts scale
    /// with core count far more aggressively than mobile parts, which are power limited.
    /// </summary>
    public double ResolveCpuTdp(CpuInfo cpu)
    {
        if (CpuTdpWattsOverride is > 0)
        {
            return CpuTdpWattsOverride.Value;
        }

        int cores = Math.Max(1, cpu.PhysicalCores);
        return cpu.IsMobile
            ? Clamp(15 + (1.5 * cores), 15, 65)
            : Clamp(35 + (4.5 * cores), 45, 250);
    }

    public double ResolveGpuTdp(GpuInfo gpu, bool isMobileSystem)
    {
        if (gpu.NominalTdpWatts is > 0)
        {
            return gpu.NominalTdpWatts.Value;
        }

        return isMobileSystem ? MobileGpuTdpWatts : DesktopGpuTdpWatts;
    }

    public double MemoryModuleWatts(MemoryModuleInfo module)
    {
        double watts = MemoryModuleBaseWatts + (Math.Max(0, module.CapacityGigabytes) * MemoryWattsPerGigabyte);

        if (module.Technology is MemoryTechnology.Ddr5 or MemoryTechnology.Lpddr5)
        {
            watts += Ddr5ModuleSurchargeWatts;
        }

        // Low power variants found in notebooks run at a much lower voltage.
        if (module.Technology is MemoryTechnology.Lpddr4 or MemoryTechnology.Lpddr5)
        {
            watts *= 0.6;
        }

        return watts;
    }

    public (double Idle, double Active) StorageWattsFor(StorageDeviceInfo device)
    {
        if (device.Media == StorageMedia.HardDisk)
        {
            return (HardDiskIdleWatts, HardDiskActiveWatts);
        }

        return device.Bus switch
        {
            StorageBus.Nvme => (NvmeIdleWatts, NvmeActiveWatts),
            StorageBus.Usb => (0.5, 2.5),
            _ => (SataSsdIdleWatts, SataSsdActiveWatts),
        };
    }

    internal static double Clamp(double value, double min, double max) =>
        double.IsNaN(value) ? min : Math.Min(max, Math.Max(min, value));

    public PowerModelOptions Clone() => (PowerModelOptions)MemberwiseClone();
}
