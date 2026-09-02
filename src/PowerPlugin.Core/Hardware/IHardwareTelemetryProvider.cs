namespace PowerPlugin.Core.Hardware;

/// <summary>
/// Abstraction over the platform specific sensor stack. Implemented by
/// PowerPlugin.Windows; the tests provide their own stub.
/// </summary>
public interface IHardwareTelemetryProvider : IDisposable
{
    /// <summary>Enumerates the machine once. Called during startup.</summary>
    HardwareInventory GetInventory();

    /// <summary>Refreshes all sensors and returns the current readings.</summary>
    HardwareTelemetry Read();
}
