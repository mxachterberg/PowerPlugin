using System.Management;
using PowerPlugin.Core.Hardware;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.Windows;

/// <summary>
/// Reads the static hardware description through WMI. Everything here is best effort:
/// a failing query degrades a single detail, never the whole application.
/// </summary>
internal static class WmiInventoryReader
{
    /// <summary>Chassis types that identify a portable machine (SMBIOS 3.4, table 3).</summary>
    private static readonly HashSet<int> PortableChassisTypes =
    [
        8,  // Portable
        9,  // Laptop
        10, // Notebook
        11, // Hand Held
        12, // Docking Station
        14, // Sub Notebook
        18, // Expansion Chassis (convertible)
        21, // Peripheral Chassis
        30, // Tablet
        31, // Convertible
        32, // Detachable
    ];

    public static CpuInfo ReadCpu(bool isMobile)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string name = GetString(item, "Name") ?? "Unbekannte CPU";
                    int cores = (int)GetNumber(item, "NumberOfCores", 0);
                    int logical = (int)GetNumber(item, "NumberOfLogicalProcessors", 0);

                    return new CpuInfo(
                        name.Trim(),
                        cores > 0 ? cores : Math.Max(1, Environment.ProcessorCount / 2),
                        logical > 0 ? logical : Environment.ProcessorCount,
                        isMobile);
                }
            }
        }
        catch (ManagementException exception)
        {
            DiagnosticsLog.Write("WMI Win32_Processor", exception);
        }

        return CpuInfo.Unknown with { IsMobile = isMobile };
    }

    public static bool DetectMobileSystem()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    if (item["ChassisTypes"] is ushort[] types && types.Any(t => PortableChassisTypes.Contains(t)))
                    {
                        return true;
                    }
                }
            }
        }
        catch (ManagementException exception)
        {
            DiagnosticsLog.Write("WMI Win32_SystemEnclosure", exception);
        }

        // A machine with a battery is portable even if the chassis type says otherwise.
        return HasBattery();
    }

    public static bool HasBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_Battery");
            using ManagementObjectCollection results = searcher.Get();
            return results.Count > 0;
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    public static IReadOnlyList<MemoryModuleInfo> ReadMemoryModules()
    {
        var modules = new List<MemoryModuleInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, SMBIOSMemoryType, Speed, DeviceLocator, BankLabel FROM Win32_PhysicalMemory");

            int index = 0;
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    double bytes = GetNumber(item, "Capacity", 0);
                    if (bytes <= 0)
                    {
                        continue;
                    }

                    string locator = GetString(item, "DeviceLocator") ?? $"DIMM{index}";

                    modules.Add(new MemoryModuleInfo(
                        $"dimm:{locator}:{index}",
                        bytes / (1024.0 * 1024.0 * 1024.0),
                        MapMemoryTechnology((int)GetNumber(item, "SMBIOSMemoryType", 0)),
                        (int)GetNumber(item, "Speed", 0)));

                    index++;
                }
            }
        }
        catch (ManagementException exception)
        {
            DiagnosticsLog.Write("WMI Win32_PhysicalMemory", exception);
        }

        return modules;
    }

    /// <summary>Maps the SMBIOS memory type code to the technology used by the power model.</summary>
    private static MemoryTechnology MapMemoryTechnology(int smbiosType) => smbiosType switch
    {
        24 => MemoryTechnology.Ddr3,
        26 => MemoryTechnology.Ddr4,
        34 => MemoryTechnology.Ddr5,
        30 => MemoryTechnology.Ddr3,    // LPDDR3
        31 => MemoryTechnology.Lpddr4,
        35 => MemoryTechnology.Lpddr5,
        _ => MemoryTechnology.Unknown,
    };

    public static double ReadTotalMemoryGigabytes()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    double bytes = GetNumber(item, "TotalPhysicalMemory", 0);
                    if (bytes > 0)
                    {
                        return bytes / (1024.0 * 1024.0 * 1024.0);
                    }
                }
            }
        }
        catch (ManagementException exception)
        {
            DiagnosticsLog.Write("WMI Win32_ComputerSystem", exception);
        }

        return 0;
    }

    /// <summary>
    /// Physical drives with bus and media type. Queried from the storage management provider,
    /// which - unlike Win32_DiskDrive - reliably distinguishes NVMe, SATA and USB.
    /// </summary>
    public static IReadOnlyList<StorageDeviceInfo> ReadStorageDevices()
    {
        var devices = new List<StorageDeviceInfo>();

        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            var query = new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk");

            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string id = GetString(item, "DeviceId") ?? devices.Count.ToString();
                    string name = GetString(item, "FriendlyName") ?? $"Laufwerk {id}";
                    double size = GetNumber(item, "Size", 0);

                    devices.Add(new StorageDeviceInfo(
                        $"disk:{id}",
                        name.Trim(),
                        MapBusType((int)GetNumber(item, "BusType", 0)),
                        MapMediaType((int)GetNumber(item, "MediaType", 0)),
                        size / (1000.0 * 1000.0 * 1000.0)));
                }
            }
        }
        catch (ManagementException exception)
        {
            DiagnosticsLog.Write("WMI MSFT_PhysicalDisk", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            DiagnosticsLog.Write("WMI MSFT_PhysicalDisk", exception);
        }

        return devices;
    }

    private static StorageBus MapBusType(int busType) => busType switch
    {
        17 => StorageBus.Nvme,
        11 => StorageBus.Sata,
        7 => StorageBus.Usb,
        _ => StorageBus.Unknown,
    };

    private static StorageMedia MapMediaType(int mediaType) => mediaType switch
    {
        3 => StorageMedia.HardDisk,
        4 => StorageMedia.SolidState,
        5 => StorageMedia.SolidState, // Storage class memory
        _ => StorageMedia.Unknown,
    };

    public static string? ReadMotherboardName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string? manufacturer = GetString(item, "Manufacturer");
                    string? product = GetString(item, "Product");

                    string combined = string.Join(' ', new[] { manufacturer, product }
                        .Where(s => !string.IsNullOrWhiteSpace(s)))
                        .Trim();

                    if (combined.Length > 0)
                    {
                        return $"Mainboard {combined}";
                    }
                }
            }
        }
        catch (ManagementException exception)
        {
            DiagnosticsLog.Write("WMI Win32_BaseBoard", exception);
        }

        return null;
    }

    private static string? GetString(ManagementBaseObject item, string property)
    {
        try
        {
            return item[property] as string;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static double GetNumber(ManagementBaseObject item, string property, double fallback)
    {
        try
        {
            object? value = item[property];
            return value is null ? fallback : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is ManagementException or InvalidCastException or FormatException or OverflowException)
        {
            return fallback;
        }
    }
}
