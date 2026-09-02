namespace PowerPlugin.Core.Hardware;

public enum MemoryTechnology
{
    Unknown,
    Ddr3,
    Ddr4,
    Ddr5,
    Lpddr4,
    Lpddr5,
}

public enum StorageBus
{
    Unknown,
    Nvme,
    Sata,
    Usb,
}

public enum StorageMedia
{
    Unknown,
    SolidState,
    HardDisk,
}

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
}

/// <summary>Static description of the CPU, gathered once at startup.</summary>
public sealed record CpuInfo(
    string Name,
    int PhysicalCores,
    int LogicalCores,
    bool IsMobile)
{
    public static CpuInfo Unknown { get; } = new("Unbekannte CPU", 4, 8, false);
}

/// <summary>Static description of one graphics adapter.</summary>
public sealed record GpuInfo(
    string Key,
    string Name,
    GpuVendor Vendor,
    bool IsIntegrated,
    double? NominalTdpWatts = null);

/// <summary>Static description of one installed memory module.</summary>
public sealed record MemoryModuleInfo(
    string Key,
    double CapacityGigabytes,
    MemoryTechnology Technology,
    int SpeedMhz);

/// <summary>Static description of one physical drive.</summary>
public sealed record StorageDeviceInfo(
    string Key,
    string Name,
    StorageBus Bus,
    StorageMedia Media,
    double CapacityGigabytes);

/// <summary>
/// Everything the estimation model needs to know about the machine that does not
/// change while the program runs.
/// </summary>
public sealed record HardwareInventory
{
    public static HardwareInventory Fallback { get; } = new()
    {
        Cpu = CpuInfo.Unknown,
        IsMobileSystem = false,
    };

    public CpuInfo Cpu { get; init; } = CpuInfo.Unknown;

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = Array.Empty<GpuInfo>();

    public IReadOnlyList<MemoryModuleInfo> MemoryModules { get; init; } = Array.Empty<MemoryModuleInfo>();

    public IReadOnlyList<StorageDeviceInfo> StorageDevices { get; init; } = Array.Empty<StorageDeviceInfo>();

    /// <summary>Number of case/CPU fans detected through the super I/O chip.</summary>
    public int FanCount { get; init; }

    /// <summary>True for notebooks, tablets and other battery powered chassis.</summary>
    public bool IsMobileSystem { get; init; }

    public string? MotherboardName { get; init; }

    /// <summary>Total installed memory, used as a fallback when no per-module data is available.</summary>
    public double TotalMemoryGigabytes { get; init; }

    public bool HasDedicatedGpu => Gpus.Any(g => !g.IsIntegrated);
}
