using PowerPlugin.Core.Estimation;
using PowerPlugin.Core.Hardware;
using PowerPlugin.Core.Model;
using Xunit;

namespace PowerPlugin.Tests;

public sealed class ComponentPowerEstimatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    private static PowerSnapshot Estimate(
        HardwareInventory inventory,
        HardwareTelemetry telemetry,
        PowerModelOptions? options = null) =>
        new ComponentPowerEstimator(options ?? new PowerModelOptions()).Estimate(inventory, telemetry, Now);

    [Fact]
    public void IdleDesktopStaysInAPlausibleRange()
    {
        PowerSnapshot snapshot = Estimate(TestData.Desktop(), TestData.Idle());

        // An idle desktop with a dedicated GPU sits somewhere between 50 W and 150 W at the socket.
        Assert.InRange(snapshot.TotalWatts, 50, 150);
    }

    [Fact]
    public void FullLoadDrawsSubstantiallyMoreThanIdle()
    {
        HardwareInventory inventory = TestData.Desktop();

        double idle = Estimate(inventory, TestData.Idle()).TotalWatts;
        double load = Estimate(inventory, TestData.FullLoad()).TotalWatts;

        Assert.True(load > idle * 2, $"Volllast ({load:0} W) sollte deutlich über Leerlauf ({idle:0} W) liegen.");
    }

    [Fact]
    public void PackageSensorIsPreferredOverTheModel()
    {
        HardwareTelemetry telemetry = TestData.Idle() with { CpuPackageWatts = 42 };

        PowerComponent cpu = Estimate(TestData.Desktop(), telemetry)
            .Components.Single(c => c.Key == ComponentPowerEstimator.CpuKey);

        Assert.Equal(PowerReadingSource.Sensor, cpu.Source);

        // The sensor value is raised by the VRM losses, so it must be a little above 42 W.
        Assert.InRange(cpu.Watts, 42, 50);
    }

    [Fact]
    public void GpuSensorIsUsedVerbatim()
    {
        HardwareTelemetry telemetry = TestData.Idle() with
        {
            GpuWatts = new Dictionary<string, double> { [TestData.DesktopGpuKey] = 123.0 },
        };

        PowerComponent gpu = Estimate(TestData.Desktop(), telemetry)
            .Components.Single(c => c.Key == TestData.DesktopGpuKey);

        Assert.Equal(PowerReadingSource.Sensor, gpu.Source);
        Assert.Equal(123.0, gpu.Watts, precision: 3);
    }

    [Fact]
    public void IntegratedGpuIsNotCountedTwiceWhenThePackageSensorExists()
    {
        HardwareInventory notebook = TestData.Notebook();

        PowerSnapshot withSensor = Estimate(notebook, TestData.Idle() with { CpuPackageWatts = 15 });
        Assert.DoesNotContain(withSensor.Components, c => c.Key == TestData.IntegratedGpuKey);

        // Without a package sensor the iGPU has to be modelled explicitly.
        PowerSnapshot withoutSensor = Estimate(notebook, TestData.Idle());
        Assert.Contains(withoutSensor.Components, c => c.Key == TestData.IntegratedGpuKey);
    }

    [Fact]
    public void ComponentsBelowTheThresholdAreSummedInsteadOfListed()
    {
        var options = new PowerModelOptions { ReportingThresholdWatts = 1.0 };

        // A single small SATA SSD idles at 0.35 W and must not appear as its own row.
        var inventory = new HardwareInventory
        {
            Cpu = new CpuInfo("Test", 4, 8, false),
            StorageDevices = [new StorageDeviceInfo("disk:ssd", "Kleine SSD", StorageBus.Sata, StorageMedia.SolidState, 250)],
            TotalMemoryGigabytes = 8,
        };

        PowerSnapshot snapshot = Estimate(inventory, TestData.Idle(), options);

        Assert.DoesNotContain(snapshot.Components, c => c.Key == "disk:ssd");
        Assert.True(snapshot.BelowThresholdWatts > 0);
        Assert.All(snapshot.Components, c => Assert.True(c.Watts > options.ReportingThresholdWatts));
    }

    [Fact]
    public void BelowThresholdEnergyStillCountsTowardsTheTotal()
    {
        var inventory = new HardwareInventory
        {
            Cpu = new CpuInfo("Test", 4, 8, false),
            StorageDevices = [new StorageDeviceInfo("disk:ssd", "Kleine SSD", StorageBus.Sata, StorageMedia.SolidState, 250)],
            TotalMemoryGigabytes = 8,
        };

        PowerSnapshot snapshot = Estimate(inventory, TestData.Idle());

        double listed = snapshot.Components.Sum(c => c.Watts);
        Assert.Equal(listed + snapshot.BelowThresholdWatts, snapshot.TotalWatts, precision: 6);
    }

    [Fact]
    public void ConversionLossesMatchTheConfiguredEfficiency()
    {
        var options = new PowerModelOptions { PowerSupplyEfficiency = 0.80, IncludeConversionLosses = true };
        PowerSnapshot snapshot = Estimate(TestData.Desktop(), TestData.Idle(), options);

        double dcWatts = snapshot.TotalWatts - snapshot.ConversionLossWatts;

        // At 80 % efficiency the socket has to deliver 25 % more than the components consume.
        Assert.Equal(dcWatts * 0.25, snapshot.ConversionLossWatts, precision: 4);
    }

    [Fact]
    public void ConversionLossesCanBeDisabled()
    {
        var options = new PowerModelOptions { IncludeConversionLosses = false };
        PowerSnapshot snapshot = Estimate(TestData.Desktop(), TestData.Idle(), options);

        Assert.Equal(0, snapshot.ConversionLossWatts);
        Assert.DoesNotContain(snapshot.Components, c => c.Key == ComponentPowerEstimator.ConversionLossKey);
    }

    [Fact]
    public void BatteryOperationRemovesTheAdapterLosses()
    {
        HardwareTelemetry onBattery = TestData.Idle() with
        {
            IsOnAcPower = false,
            BatteryDischargeWatts = 18,
        };

        PowerSnapshot snapshot = Estimate(TestData.Notebook(), onBattery);

        Assert.Equal(0, snapshot.ConversionLossWatts);
    }

    [Fact]
    public void MeasuredBatteryPowerScalesTheModelToMatch()
    {
        HardwareTelemetry onBattery = TestData.Idle() with
        {
            IsOnAcPower = false,
            BatteryDischargeWatts = 12,
        };

        PowerSnapshot snapshot = Estimate(TestData.Notebook(), onBattery);

        Assert.Equal(12, snapshot.MeasuredSystemWatts);
        Assert.Equal(MeasurementConfidence.High, snapshot.Confidence);

        // Everything is modelled here, so the breakdown has to add up to the measurement.
        Assert.Equal(12, snapshot.TotalWatts, precision: 3);
    }

    [Fact]
    public void SensorBackedComponentsSurviveTheReconciliation()
    {
        HardwareTelemetry onBattery = TestData.Idle() with
        {
            IsOnAcPower = false,
            BatteryDischargeWatts = 30,
            CpuPackageWatts = 9,
        };

        PowerSnapshot snapshot = Estimate(TestData.Notebook(), onBattery);
        PowerComponent cpu = snapshot.Components.Single(c => c.Key == ComponentPowerEstimator.CpuKey);

        // 9 W behind a VRM at 90 % efficiency is 10 W at the input - untouched by the scaling.
        Assert.Equal(10, cpu.Watts, precision: 3);
        Assert.Equal(30, snapshot.TotalWatts, precision: 3);
    }

    [Fact]
    public void ComponentsAreOrderedByDescendingDraw()
    {
        PowerSnapshot snapshot = Estimate(TestData.Desktop(), TestData.FullLoad());

        double[] watts = snapshot.Components.Select(c => c.Watts).ToArray();
        Assert.Equal(watts.OrderByDescending(w => w).ToArray(), watts);
    }

    [Fact]
    public void ConfidenceReflectsHowMuchIsActuallyMeasured()
    {
        HardwareInventory desktop = TestData.Desktop();

        Assert.Equal(MeasurementConfidence.Low, Estimate(desktop, TestData.Idle()).Confidence);

        HardwareTelemetry cpuOnly = TestData.Idle() with { CpuPackageWatts = 30 };
        Assert.Equal(MeasurementConfidence.Low, Estimate(desktop, cpuOnly).Confidence);

        HardwareTelemetry both = cpuOnly with
        {
            GpuWatts = new Dictionary<string, double> { [TestData.DesktopGpuKey] = 40 },
        };
        Assert.Equal(MeasurementConfidence.Medium, Estimate(desktop, both).Confidence);
    }

    [Fact]
    public void ExternalMonitorsAreNotPartOfADesktopBreakdown()
    {
        PowerSnapshot snapshot = Estimate(TestData.Desktop(), TestData.Idle());

        Assert.DoesNotContain(snapshot.Components, c => c.Category == ComponentCategory.Display);
    }

    [Fact]
    public void NotebookPanelScalesWithBrightness()
    {
        HardwareInventory notebook = TestData.Notebook();

        double dark = Estimate(notebook, TestData.Idle() with { DisplayBrightness = 0 })
            .Components.Single(c => c.Key == ComponentPowerEstimator.DisplayKey).Watts;

        double bright = Estimate(notebook, TestData.Idle() with { DisplayBrightness = 1 })
            .Components.Single(c => c.Key == ComponentPowerEstimator.DisplayKey).Watts;

        Assert.True(bright > dark);
    }

    [Fact]
    public void MemoryPowerGrowsWithTheNumberOfModules()
    {
        HardwareInventory two = TestData.Desktop();
        HardwareInventory one = two with { MemoryModules = [two.MemoryModules[0]] };

        double twoModules = Estimate(two, TestData.Idle())
            .Components.Single(c => c.Key == ComponentPowerEstimator.MemoryKey).Watts;

        double oneModule = Estimate(one, TestData.Idle())
            .Components.Single(c => c.Key == ComponentPowerEstimator.MemoryKey).Watts;

        Assert.Equal(oneModule * 2, twoModules, precision: 4);
    }

    [Fact]
    public void HardDisksAreModelledAsHungrierThanSolidStateDrives()
    {
        PowerSnapshot snapshot = Estimate(TestData.Desktop(), TestData.Idle());

        double hdd = snapshot.Components.Single(c => c.Key == TestData.HddKey).Watts;
        double nvme = snapshot.Components.Single(c => c.Key == TestData.NvmeKey).Watts;

        Assert.True(hdd > nvme);
    }

    [Fact]
    public void UnknownHardwareDoesNotProduceNonsense()
    {
        PowerSnapshot snapshot = Estimate(HardwareInventory.Fallback, HardwareTelemetry.Empty);

        Assert.True(snapshot.TotalWatts > 0);
        Assert.True(snapshot.TotalWatts < 500);
        Assert.All(snapshot.Components, c => Assert.True(double.IsFinite(c.Watts) && c.Watts >= 0));
    }
}
