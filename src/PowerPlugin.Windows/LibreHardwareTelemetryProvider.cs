using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using PowerPlugin.Core.Hardware;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.Windows;

/// <summary>
/// Reads the machine through LibreHardwareMonitor and enriches it with WMI data.
/// <para>
/// Power sensors for the CPU package come from the RAPL/SMU model specific registers, which
/// require administrator rights. Without them the provider still delivers utilisation for every
/// device, and the estimation model fills the gap.
/// </para>
/// </summary>
public sealed class LibreHardwareTelemetryProvider : IHardwareTelemetryProvider
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly SystemPowerStateReader _powerState = new();
    private readonly DisplayBrightnessReader _brightness = new();

    /// <summary>Highest fan speed seen so far, used to normalise the fan load.</summary>
    private readonly Dictionary<string, double> _maxFanRpm = new(StringComparer.Ordinal);

    private readonly bool _isElevated = IsProcessElevated();
    private bool _opened;
    private bool _isMobileSystem;
    private bool _disposed;

    public LibreHardwareTelemetryProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true,
            IsBatteryEnabled = true,
            IsPsuEnabled = true,
            IsNetworkEnabled = false,
        };
    }

    public HardwareInventory GetInventory()
    {
        EnsureOpen();

        _isMobileSystem = WmiInventoryReader.DetectMobileSystem();

        var gpus = new List<GpuInfo>();
        var storageFromSensors = new List<StorageDeviceInfo>();
        int fanCount = 0;

        if (_opened)
        {
            foreach (IHardware hardware in _computer.Hardware)
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        gpus.Add(CreateGpuInfo(hardware));
                        break;

                    case HardwareType.Storage:
                        storageFromSensors.Add(new StorageDeviceInfo(
                            hardware.Identifier.ToString(),
                            hardware.Name,
                            StorageBus.Unknown,
                            StorageMedia.Unknown,
                            0));
                        break;

                    case HardwareType.Motherboard:
                    case HardwareType.SuperIO:
                        fanCount += CountFans(hardware);
                        break;
                }
            }
        }

        IReadOnlyList<StorageDeviceInfo> storage = MergeStorage(
            storageFromSensors, WmiInventoryReader.ReadStorageDevices());

        return new HardwareInventory
        {
            Cpu = WmiInventoryReader.ReadCpu(_isMobileSystem),
            Gpus = gpus,
            MemoryModules = WmiInventoryReader.ReadMemoryModules(),
            StorageDevices = storage,
            FanCount = fanCount,
            IsMobileSystem = _isMobileSystem,
            MotherboardName = WmiInventoryReader.ReadMotherboardName(),
            TotalMemoryGigabytes = WmiInventoryReader.ReadTotalMemoryGigabytes(),
        };
    }

    public HardwareTelemetry Read()
    {
        EnsureOpen();

        double cpuLoad = 0;
        double? cpuPackageWatts = null;
        double memoryLoad = 0;
        double? psuWatts = null;

        var gpuLoads = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var gpuWatts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var storageActivity = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var fanLoads = new List<double>();
        var additional = new List<PowerSensorReading>();

        if (_opened)
        {
            _computer.Accept(_visitor);

            foreach (IHardware hardware in _computer.Hardware)
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        cpuLoad = ReadCpuLoad(hardware);
                        cpuPackageWatts = ReadCpuPackageWatts(hardware);
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                    {
                        string key = hardware.Identifier.ToString();
                        gpuLoads[key] = ReadGpuLoad(hardware);

                        if (ReadGpuWatts(hardware) is { } watts)
                        {
                            gpuWatts[key] = watts;
                        }

                        break;
                    }

                    case HardwareType.Memory:
                        memoryLoad = ReadMemoryLoad(hardware);
                        break;

                    case HardwareType.Storage:
                        storageActivity[hardware.Identifier.ToString()] = ReadStorageActivity(hardware);
                        break;

                    case HardwareType.Psu:
                        psuWatts = ReadPsuWatts(hardware);
                        break;

                    case HardwareType.Motherboard:
                    case HardwareType.SuperIO:
                        CollectFanLoads(hardware, fanLoads);
                        CollectAdditionalPowerSensors(hardware, additional);
                        break;
                }
            }
        }

        BatteryState battery = _powerState.Read();

        return new HardwareTelemetry
        {
            CpuLoad = cpuLoad,
            CpuPackageWatts = cpuPackageWatts,
            GpuLoads = gpuLoads,
            GpuWatts = gpuWatts,
            MemoryLoad = memoryLoad,
            StorageActivity = storageActivity,
            FanLoads = fanLoads,
            AdditionalPowerSensors = additional,
            PsuOutputWatts = psuWatts,
            BatteryDischargeWatts = battery.DischargeWatts,
            IsOnAcPower = battery.IsOnAcPower,
            DisplayBrightness = _isMobileSystem ? _brightness.Read() : null,

            // Without the kernel driver there is no RAPL access, so the CPU package power
            // is missing and the model has to estimate it.
            RequiresElevation = !_isElevated && cpuPackageWatts is null,
        };
    }

    // ---- Sensor helpers -----------------------------------------------------------

    private static double ReadCpuLoad(IHardware hardware) =>
        FindSensor(hardware, SensorType.Load, "CPU Total") / 100.0;

    /// <summary>
    /// Package power covers cores, cache, memory controller and - where present - the
    /// integrated GPU. The per-core sensors are deliberately ignored: adding them to the
    /// package value would count the same watts twice.
    /// </summary>
    private static double? ReadCpuPackageWatts(IHardware hardware)
    {
        foreach (string name in new[] { "CPU Package", "Package", "CPU PPT", "Package Power" })
        {
            ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.Power &&
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

            if (sensor?.Value is > 0 and < 1000)
            {
                return sensor.Value;
            }
        }

        return null;
    }

    private static double ReadGpuLoad(IHardware hardware)
    {
        foreach (string name in new[] { "GPU Core", "GPU Total", "D3D 3D" })
        {
            ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.Load &&
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

            if (sensor?.Value is { } value)
            {
                return Math.Clamp(value / 100.0, 0, 1);
            }
        }

        return 0;
    }

    /// <summary>
    /// Prefers the total board power reported by NVML or the AMD driver. That value already
    /// includes the memory and the board's own voltage regulators.
    /// </summary>
    private static double? ReadGpuWatts(IHardware hardware)
    {
        foreach (string name in new[] { "GPU Package", "GPU Power", "GPU Total", "GPU PPT", "Board Power" })
        {
            ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.Power &&
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

            if (sensor?.Value is > 0 and < 2000)
            {
                return sensor.Value;
            }
        }

        return null;
    }

    private static double ReadMemoryLoad(IHardware hardware)
    {
        ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Load &&
            s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase));

        return sensor?.Value is { } value ? Math.Clamp(value / 100.0, 0, 1) : 0;
    }

    private static double ReadStorageActivity(IHardware hardware)
    {
        ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Load &&
            s.Name.Contains("Total Activity", StringComparison.OrdinalIgnoreCase));

        return sensor?.Value is { } value ? Math.Clamp(value / 100.0, 0, 1) : 0;
    }

    private static double? ReadPsuWatts(IHardware hardware)
    {
        ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Power &&
            (s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
             s.Name.Contains("Output", StringComparison.OrdinalIgnoreCase)));

        return sensor?.Value is > 5 and < 2000 ? sensor.Value : null;
    }

    private void CollectFanLoads(IHardware hardware, List<double> fanLoads)
    {
        foreach (IHardware sub in hardware.SubHardware)
        {
            CollectFanLoads(sub, fanLoads);
        }

        foreach (ISensor sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Fan))
        {
            if (sensor.Value is not { } rpm || rpm <= 0)
            {
                continue;
            }

            string key = sensor.Identifier.ToString();

            // A fan that has never been seen above ~1200 rpm is probably running slowly,
            // not at full speed, so the reference speed has a floor.
            double reference = Math.Max(1200, _maxFanRpm.TryGetValue(key, out double max) ? max : 0);
            reference = Math.Max(reference, rpm);
            _maxFanRpm[key] = reference;

            fanLoads.Add(Math.Clamp(rpm / reference, 0, 1));
        }
    }

    /// <summary>
    /// Picks up board level power sensors that are not already covered by the CPU or GPU
    /// readings, for example chipset or SoC rails on boards with an embedded controller.
    /// </summary>
    private static void CollectAdditionalPowerSensors(IHardware hardware, List<PowerSensorReading> readings)
    {
        foreach (IHardware sub in hardware.SubHardware)
        {
            CollectAdditionalPowerSensors(sub, readings);
        }

        foreach (ISensor sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Power))
        {
            if (sensor.Value is not { } watts || watts <= 0)
            {
                continue;
            }

            string name = sensor.Name;
            if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            readings.Add(new PowerSensorReading(sensor.Identifier.ToString(), watts, name));
        }
    }

    private static int CountFans(IHardware hardware)
    {
        int count = hardware.Sensors.Count(s => s.SensorType == SensorType.Fan);

        foreach (IHardware sub in hardware.SubHardware)
        {
            count += CountFans(sub);
        }

        return count;
    }

    private static double FindSensor(IHardware hardware, SensorType type, string name)
    {
        ISensor? sensor = hardware.Sensors.FirstOrDefault(s =>
            s.SensorType == type && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        return sensor?.Value ?? 0;
    }

    // ---- Inventory helpers --------------------------------------------------------

    private static GpuInfo CreateGpuInfo(IHardware hardware)
    {
        GpuVendor vendor = hardware.HardwareType switch
        {
            HardwareType.GpuNvidia => GpuVendor.Nvidia,
            HardwareType.GpuAmd => GpuVendor.Amd,
            HardwareType.GpuIntel => GpuVendor.Intel,
            _ => GpuVendor.Unknown,
        };

        return new GpuInfo(
            hardware.Identifier.ToString(),
            hardware.Name,
            vendor,
            IsIntegratedGpu(hardware.Name, vendor));
    }

    /// <summary>
    /// Distinguishes an integrated from a dedicated GPU by name. Integrated graphics share the
    /// CPU package power budget and must not be counted a second time.
    /// </summary>
    internal static bool IsIntegratedGpu(string name, GpuVendor vendor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return vendor switch
        {
            // Intel graphics are integrated except for the discrete Arc series.
            GpuVendor.Intel => !name.Contains("Arc", StringComparison.OrdinalIgnoreCase),

            // AMD APUs report names such as "AMD Radeon(TM) Graphics" or "Radeon Vega Graphics",
            // while dedicated cards carry an RX, Pro or Instinct model number.
            GpuVendor.Amd => name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) &&
                             !name.Contains("RX", StringComparison.OrdinalIgnoreCase) &&
                             !name.Contains("Pro W", StringComparison.OrdinalIgnoreCase),

            _ => false,
        };
    }

    /// <summary>
    /// Combines the drives seen by the sensor stack with the bus and media information from WMI.
    /// The sensor identifier is kept as the key so the activity readings still line up.
    /// </summary>
    private static IReadOnlyList<StorageDeviceInfo> MergeStorage(
        IReadOnlyList<StorageDeviceInfo> fromSensors,
        IReadOnlyList<StorageDeviceInfo> fromWmi)
    {
        if (fromSensors.Count == 0)
        {
            return fromWmi;
        }

        var remaining = fromWmi.ToList();
        var merged = new List<StorageDeviceInfo>(fromSensors.Count);

        foreach (StorageDeviceInfo sensorDevice in fromSensors)
        {
            StorageDeviceInfo? match = remaining.FirstOrDefault(w =>
                string.Equals(w.Name, sensorDevice.Name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                merged.Add(sensorDevice);
                continue;
            }

            remaining.Remove(match);
            merged.Add(sensorDevice with
            {
                Bus = match.Bus,
                Media = match.Media,
                CapacityGigabytes = match.CapacityGigabytes,
            });
        }

        // Drives WMI knows about but the sensor stack does not, e.g. external USB disks.
        merged.AddRange(remaining);
        return merged;
    }

    private void EnsureOpen()
    {
        if (_opened || _disposed)
        {
            return;
        }

        try
        {
            _computer.Open();
            _opened = true;

            DiagnosticsLog.Write(_isElevated
                ? "Sensor-Backend geöffnet (mit Administratorrechten)."
                : "Sensor-Backend geöffnet (ohne Administratorrechte - CPU-Leistungssensoren nicht verfügbar).");
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Sensor-Backend konnte nicht geöffnet werden", exception);
            _opened = false;
        }
    }

    internal static bool IsProcessElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_opened)
        {
            return;
        }

        try
        {
            _computer.Close();
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Sensor-Backend schließen", exception);
        }
    }

    /// <summary>Refreshes every hardware node and its children before the values are read.</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();

            foreach (IHardware sub in hardware.SubHardware)
            {
                sub.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
