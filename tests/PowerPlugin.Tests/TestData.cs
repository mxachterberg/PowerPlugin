using PowerPlugin.Core.Hardware;

namespace PowerPlugin.Tests;

/// <summary>Reusable hardware descriptions for the tests.</summary>
internal static class TestData
{
    public const string DesktopGpuKey = "gpu-nvidia-0";
    public const string IntegratedGpuKey = "gpu-intel-0";
    public const string NvmeKey = "disk:nvme0";
    public const string HddKey = "disk:hdd0";

    public static HardwareInventory Desktop() => new()
    {
        Cpu = new CpuInfo("Test Desktop CPU", PhysicalCores: 8, LogicalCores: 16, IsMobile: false),
        Gpus = [new GpuInfo(DesktopGpuKey, "Test GeForce", GpuVendor.Nvidia, IsIntegrated: false)],
        MemoryModules =
        [
            new MemoryModuleInfo("dimm0", 16, MemoryTechnology.Ddr4, 3200),
            new MemoryModuleInfo("dimm1", 16, MemoryTechnology.Ddr4, 3200),
        ],
        StorageDevices =
        [
            new StorageDeviceInfo(NvmeKey, "Test NVMe", StorageBus.Nvme, StorageMedia.SolidState, 1000),
            new StorageDeviceInfo(HddKey, "Test HDD", StorageBus.Sata, StorageMedia.HardDisk, 4000),
        ],
        FanCount = 4,
        IsMobileSystem = false,
        MotherboardName = "Test Board",
        TotalMemoryGigabytes = 32,
    };

    public static HardwareInventory Notebook() => new()
    {
        Cpu = new CpuInfo("Test Mobile CPU", PhysicalCores: 6, LogicalCores: 12, IsMobile: true),
        Gpus = [new GpuInfo(IntegratedGpuKey, "Test Iris Graphics", GpuVendor.Intel, IsIntegrated: true)],
        MemoryModules = [new MemoryModuleInfo("dimm0", 16, MemoryTechnology.Lpddr5, 6400)],
        StorageDevices = [new StorageDeviceInfo(NvmeKey, "Test NVMe", StorageBus.Nvme, StorageMedia.SolidState, 512)],
        FanCount = 1,
        IsMobileSystem = true,
        TotalMemoryGigabytes = 16,
    };

    public static HardwareTelemetry Idle() => new()
    {
        CpuLoad = 0.03,
        MemoryLoad = 0.35,
        IsOnAcPower = true,
    };

    public static HardwareTelemetry FullLoad() => new()
    {
        CpuLoad = 1.0,
        MemoryLoad = 0.8,
        GpuLoads = new Dictionary<string, double> { [DesktopGpuKey] = 1.0 },
        StorageActivity = new Dictionary<string, double> { [NvmeKey] = 0.9, [HddKey] = 0.6 },
        FanLoads = [1.0, 1.0, 1.0, 1.0],
        IsOnAcPower = true,
    };
}
