using System.Collections.Immutable;
using PowerPlugin.Core.Hardware;
using PowerPlugin.Core.Model;

namespace PowerPlugin.Core.Estimation;

/// <summary>
/// Turns raw hardware telemetry into a <see cref="PowerSnapshot"/>.
/// <para>
/// The estimator always prefers a real power sensor over the model. Components without a
/// sensor are approximated from their utilisation and the coefficients in
/// <see cref="PowerModelOptions"/>. When the machine reports a measured system power - a
/// notebook running on battery, or a power supply with telemetry - the modelled components
/// are scaled so that the breakdown adds up to that measurement.
/// </para>
/// </summary>
public sealed class ComponentPowerEstimator
{
    public const string CpuKey = "cpu";
    public const string MemoryKey = "memory";
    public const string MainboardKey = "mainboard";
    public const string CoolingKey = "cooling";
    public const string DisplayKey = "display";
    public const string ConversionLossKey = "conversion-loss";

    private readonly PowerModelOptions _options;

    public ComponentPowerEstimator(PowerModelOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public PowerSnapshot Estimate(HardwareInventory inventory, HardwareTelemetry telemetry, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(telemetry);

        var components = new List<PowerComponent>();

        components.Add(EstimateCpu(inventory, telemetry));
        components.AddRange(EstimateGpus(inventory, telemetry));
        components.Add(EstimateMemory(inventory, telemetry));
        components.AddRange(EstimateStorage(inventory, telemetry));
        components.Add(EstimateMainboard(inventory));
        components.Add(EstimateCooling(inventory, telemetry));

        PowerComponent? display = EstimateDisplay(inventory, telemetry);
        if (display is not null)
        {
            components.Add(display);
        }

        components.AddRange(EstimateAdditionalSensors(telemetry));

        // A measured system power beats the sum of the parts: use it to correct the model.
        double? measuredSystemWatts = ResolveMeasuredSystemWatts(telemetry);
        if (measuredSystemWatts is > 0)
        {
            ReconcileWithMeasurement(components, measuredSystemWatts.Value);
        }

        double conversionLossWatts = 0;
        if (_options.IncludeConversionLosses)
        {
            conversionLossWatts = CalculateConversionLoss(components.Sum(c => c.Watts), inventory, telemetry);
            if (conversionLossWatts > 0)
            {
                components.Add(new PowerComponent(
                    ConversionLossKey,
                    inventory.IsMobileSystem ? "Netzteil-Verluste (Adapter)" : "Netzteil-Verluste",
                    ComponentCategory.PowerSupply,
                    conversionLossWatts,
                    PowerReadingSource.Modeled,
                    $"Wandlungsverluste bei {_options.EfficiencyFor(inventory.IsMobileSystem) * 100:0.#} % Wirkungsgrad"));
            }
        }

        // Everything at or below the reporting threshold is folded into a single remainder
        // so the breakdown only lists consumers that actually matter.
        double threshold = Math.Max(0, _options.ReportingThresholdWatts);
        var reported = ImmutableArray.CreateBuilder<PowerComponent>();
        double belowThreshold = 0;

        foreach (PowerComponent component in components.OrderByDescending(c => c.Watts))
        {
            if (component.Watts > threshold)
            {
                reported.Add(component);
            }
            else if (component.Watts > 0)
            {
                belowThreshold += component.Watts;
            }
        }

        return new PowerSnapshot(
            timestamp,
            reported.ToImmutable(),
            belowThreshold,
            conversionLossWatts,
            measuredSystemWatts,
            DetermineConfidence(inventory, telemetry, measuredSystemWatts));
    }

    // ---- CPU ---------------------------------------------------------------------

    private PowerComponent EstimateCpu(HardwareInventory inventory, HardwareTelemetry telemetry)
    {
        CpuInfo cpu = inventory.Cpu;
        double load = PowerModelOptions.Clamp(telemetry.CpuLoad, 0, 1);
        double vrmEfficiency = PowerModelOptions.Clamp(_options.CpuVrmEfficiency, 0.5, 1.0);

        if (telemetry.CpuPackageWatts is > 0 and < 1000)
        {
            // Package sensors report the power behind the mainboard VRM, so the draw
            // seen by the power supply is slightly higher.
            double watts = telemetry.CpuPackageWatts.Value / vrmEfficiency;
            return new PowerComponent(
                CpuKey,
                cpu.Name,
                ComponentCategory.Cpu,
                watts,
                PowerReadingSource.Sensor,
                $"Package-Sensor {telemetry.CpuPackageWatts.Value:0.#} W · {load * 100:0} % Last · inkl. VRM-Verluste");
        }

        double idle = cpu.IsMobile ? _options.MobileCpuIdleWatts : _options.DesktopCpuIdleWatts;
        double tdp = Math.Max(idle, _options.ResolveCpuTdp(cpu));
        double estimated = (idle + ((tdp - idle) * Math.Pow(load, Math.Max(0.1, _options.CpuLoadExponent)))) / vrmEfficiency;

        return new PowerComponent(
            CpuKey,
            cpu.Name,
            ComponentCategory.Cpu,
            estimated,
            PowerReadingSource.Estimated,
            $"{load * 100:0} % Last · Modell {idle:0.#}-{tdp:0.#} W (kein Sensor verfügbar)");
    }

    // ---- GPU ---------------------------------------------------------------------

    private IEnumerable<PowerComponent> EstimateGpus(HardwareInventory inventory, HardwareTelemetry telemetry)
    {
        bool cpuPackageMeasured = telemetry.CpuPackageWatts is > 0;

        foreach (GpuInfo gpu in inventory.Gpus)
        {
            double load = PowerModelOptions.Clamp(
                telemetry.GpuLoads.TryGetValue(gpu.Key, out double l) ? l : 0, 0, 1);

            if (telemetry.GpuWatts.TryGetValue(gpu.Key, out double sensorWatts) && sensorWatts is > 0 and < 2000)
            {
                yield return new PowerComponent(
                    gpu.Key,
                    gpu.Name,
                    ComponentCategory.Gpu,
                    sensorWatts,
                    PowerReadingSource.Sensor,
                    $"Board-Power-Sensor · {load * 100:0} % Last");
                continue;
            }

            if (gpu.IsIntegrated)
            {
                // The iGPU shares the package power budget with the CPU cores. If the package
                // sensor already covers it, reporting it again would double count.
                if (cpuPackageMeasured)
                {
                    continue;
                }

                double integrated = _options.IntegratedGpuWattsWhenNoPackageSensor * (0.4 + (0.6 * load));
                yield return new PowerComponent(
                    gpu.Key,
                    gpu.Name,
                    ComponentCategory.Gpu,
                    integrated,
                    PowerReadingSource.Estimated,
                    $"Integrierte Grafik · {load * 100:0} % Last");
                continue;
            }

            double idle = inventory.IsMobileSystem ? _options.MobileGpuIdleWatts : _options.DesktopGpuIdleWatts;
            double tdp = Math.Max(idle, _options.ResolveGpuTdp(gpu, inventory.IsMobileSystem));
            double watts = idle + ((tdp - idle) * Math.Pow(load, Math.Max(0.1, _options.GpuLoadExponent)));

            yield return new PowerComponent(
                gpu.Key,
                gpu.Name,
                ComponentCategory.Gpu,
                watts,
                PowerReadingSource.Estimated,
                $"{load * 100:0} % Last · Modell {idle:0.#}-{tdp:0.#} W (kein Sensor verfügbar)");
        }
    }

    // ---- Memory ------------------------------------------------------------------

    private PowerComponent EstimateMemory(HardwareInventory inventory, HardwareTelemetry telemetry)
    {
        IReadOnlyList<MemoryModuleInfo> modules = inventory.MemoryModules.Count > 0
            ? inventory.MemoryModules
            : SynthesizeModules(inventory.TotalMemoryGigabytes);

        double idleShare = PowerModelOptions.Clamp(_options.MemoryIdleShare, 0, 1);
        double activityFactor = idleShare + ((1 - idleShare) * PowerModelOptions.Clamp(telemetry.MemoryLoad, 0, 1));
        double watts = modules.Sum(_options.MemoryModuleWatts) * activityFactor;

        return new PowerComponent(
            MemoryKey,
            "Arbeitsspeicher",
            ComponentCategory.Memory,
            watts,
            PowerReadingSource.Estimated,
            $"{DescribeModules(modules)} · {telemetry.MemoryLoad * 100:0} % belegt");
    }

    private static IReadOnlyList<MemoryModuleInfo> SynthesizeModules(double totalGigabytes)
    {
        double total = totalGigabytes > 0 ? totalGigabytes : 16;
        int count = Math.Max(1, (int)Math.Round(total / 16.0, MidpointRounding.AwayFromZero));

        return Enumerable.Range(0, count)
            .Select(i => new MemoryModuleInfo($"dimm{i}", total / count, MemoryTechnology.Unknown, 0))
            .ToArray();
    }

    private static string DescribeModules(IReadOnlyList<MemoryModuleInfo> modules)
    {
        if (modules.Count == 0)
        {
            return "unbekannte Bestückung";
        }

        MemoryTechnology technology = modules[0].Technology;
        string type = technology == MemoryTechnology.Unknown ? string.Empty : " " + technology.ToString().ToUpperInvariant();
        return $"{modules.Count} × {modules[0].CapacityGigabytes:0.#} GB{type}";
    }

    // ---- Storage -----------------------------------------------------------------

    private IEnumerable<PowerComponent> EstimateStorage(HardwareInventory inventory, HardwareTelemetry telemetry)
    {
        foreach (StorageDeviceInfo device in inventory.StorageDevices)
        {
            (double idle, double active) = _options.StorageWattsFor(device);

            bool hasActivity = telemetry.StorageActivity.TryGetValue(device.Key, out double activity);
            if (!hasActivity)
            {
                activity = _options.DefaultStorageActivity;
            }

            activity = PowerModelOptions.Clamp(activity, 0, 1);
            double watts = idle + ((active - idle) * activity);

            yield return new PowerComponent(
                device.Key,
                device.Name,
                ComponentCategory.Storage,
                watts,
                PowerReadingSource.Estimated,
                $"{DescribeStorage(device)} · {activity * 100:0} % Aktivität");
        }
    }

    private static string DescribeStorage(StorageDeviceInfo device)
    {
        string bus = device.Bus switch
        {
            StorageBus.Nvme => "NVMe",
            StorageBus.Sata => "SATA",
            StorageBus.Usb => "USB",
            _ => "unbekannter Bus",
        };

        string media = device.Media switch
        {
            StorageMedia.HardDisk => "HDD",
            StorageMedia.SolidState => "SSD",
            _ => "Laufwerk",
        };

        return device.CapacityGigabytes > 0
            ? $"{bus} {media}, {device.CapacityGigabytes:0} GB"
            : $"{bus} {media}";
    }

    // ---- Mainboard, cooling, display ---------------------------------------------

    private PowerComponent EstimateMainboard(HardwareInventory inventory)
    {
        double watts = _options.BaseWattsFor(inventory.IsMobileSystem);
        string name = inventory.MotherboardName is { Length: > 0 } board ? board : "Mainboard & Chipsatz";

        return new PowerComponent(
            MainboardKey,
            name,
            ComponentCategory.Mainboard,
            watts,
            PowerReadingSource.Modeled,
            "Chipsatz, Spannungswandler im Leerlauf, USB, Audio und Netzwerk");
    }

    private PowerComponent EstimateCooling(HardwareInventory inventory, HardwareTelemetry telemetry)
    {
        double perFan = Math.Max(0, _options.WattsPerFanAtFullSpeed);
        double floor = PowerModelOptions.Clamp(_options.FanMinimumLoad, 0, 1);

        if (telemetry.FanLoads.Count > 0)
        {
            double watts = telemetry.FanLoads.Sum(load => perFan * Math.Max(floor, PowerModelOptions.Clamp(load, 0, 1)));
            return new PowerComponent(
                CoolingKey,
                "Lüfter & Kühlung",
                ComponentCategory.Cooling,
                watts,
                PowerReadingSource.Estimated,
                $"{telemetry.FanLoads.Count} Lüfter · Ø {telemetry.FanLoads.Average() * 100:0} % Drehzahl");
        }

        int assumedFans = inventory.FanCount > 0
            ? inventory.FanCount
            : (inventory.IsMobileSystem ? 1 : 3);

        return new PowerComponent(
            CoolingKey,
            "Lüfter & Kühlung",
            ComponentCategory.Cooling,
            assumedFans * perFan * floor,
            PowerReadingSource.Modeled,
            $"{assumedFans} Lüfter angenommen (keine Drehzahlsensoren)");
    }

    private PowerComponent? EstimateDisplay(HardwareInventory inventory, HardwareTelemetry telemetry)
    {
        // Only the built-in panel of a notebook is powered by the machine itself.
        // External monitors have their own power cord and are deliberately not counted.
        if (!inventory.IsMobileSystem)
        {
            return null;
        }

        double brightness = PowerModelOptions.Clamp(telemetry.DisplayBrightness ?? 0.7, 0, 1);
        double watts = _options.MobileDisplayBaseWatts + (_options.MobileDisplayBacklightWatts * brightness);

        return new PowerComponent(
            DisplayKey,
            "Interner Bildschirm",
            ComponentCategory.Display,
            watts,
            PowerReadingSource.Estimated,
            $"{brightness * 100:0} % Helligkeit");
    }

    private static IEnumerable<PowerComponent> EstimateAdditionalSensors(HardwareTelemetry telemetry)
    {
        foreach (PowerSensorReading reading in telemetry.AdditionalPowerSensors)
        {
            if (reading.Watts is <= 0 or > 2000)
            {
                continue;
            }

            yield return new PowerComponent(
                $"sensor:{reading.Key}",
                reading.SensorName,
                ComponentCategory.Other,
                reading.Watts,
                PowerReadingSource.Sensor,
                "Zusätzlicher Hardware-Sensor");
        }
    }

    // ---- Measurement and conversion ----------------------------------------------

    private static double? ResolveMeasuredSystemWatts(HardwareTelemetry telemetry)
    {
        if (!telemetry.IsOnAcPower && telemetry.BatteryDischargeWatts is > 0.5 and < 500)
        {
            return telemetry.BatteryDischargeWatts;
        }

        if (telemetry.PsuOutputWatts is > 5 and < 2000)
        {
            return telemetry.PsuOutputWatts;
        }

        return null;
    }

    /// <summary>
    /// Scales the modelled components so the breakdown adds up to a measured system power.
    /// Sensor readings are left untouched - they are already ground truth.
    /// </summary>
    private static void ReconcileWithMeasurement(List<PowerComponent> components, double measuredWatts)
    {
        double measuredPart = components.Where(c => c.Source.IsMeasured()).Sum(c => c.Watts);
        double modelledPart = components.Where(c => !c.Source.IsMeasured()).Sum(c => c.Watts);

        if (modelledPart <= 0.01)
        {
            return;
        }

        double remainder = measuredWatts - measuredPart;
        double factor = remainder <= 0 ? 0.25 : PowerModelOptions.Clamp(remainder / modelledPart, 0.25, 4.0);

        for (int i = 0; i < components.Count; i++)
        {
            PowerComponent component = components[i];
            if (component.Source.IsMeasured())
            {
                continue;
            }

            components[i] = component with
            {
                Watts = component.Watts * factor,
                Detail = component.Detail is null
                    ? "an Systemmessung angeglichen"
                    : component.Detail + " · an Systemmessung angeglichen",
            };
        }
    }

    private double CalculateConversionLoss(double dcWatts, HardwareInventory inventory, HardwareTelemetry telemetry)
    {
        // Running on battery means there is no AC adapter in the chain.
        if (!telemetry.IsOnAcPower)
        {
            return 0;
        }

        double efficiency = _options.EfficiencyFor(inventory.IsMobileSystem);
        return efficiency >= 1.0 ? 0 : dcWatts * ((1.0 / efficiency) - 1.0);
    }

    private static MeasurementConfidence DetermineConfidence(
        HardwareInventory inventory,
        HardwareTelemetry telemetry,
        double? measuredSystemWatts)
    {
        if (measuredSystemWatts is > 0)
        {
            return MeasurementConfidence.High;
        }

        bool cpuMeasured = telemetry.CpuPackageWatts is > 0;
        bool gpuCovered = !inventory.HasDedicatedGpu
            || inventory.Gpus.Where(g => !g.IsIntegrated).All(g => telemetry.GpuWatts.ContainsKey(g.Key));

        return cpuMeasured && gpuCovered ? MeasurementConfidence.Medium : MeasurementConfidence.Low;
    }
}
