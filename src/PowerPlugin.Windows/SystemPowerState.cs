using System.Management;
using System.Runtime.InteropServices;
using PowerPlugin.Core.Monitoring;

namespace PowerPlugin.Windows;

/// <summary>Mains status and, on notebooks, the measured battery discharge rate.</summary>
/// <param name="IsOnAcPower">True while the machine is connected to mains.</param>
/// <param name="DischargeWatts">Discharge rate in watts, only while running on battery.</param>
internal readonly record struct BatteryState(bool IsOnAcPower, double? DischargeWatts);

/// <summary>
/// Reads the AC line status and the battery discharge rate.
/// <para>
/// The discharge rate is the only figure on a consumer PC that measures the real power of the
/// whole system, so it is used to calibrate the estimation model whenever it is available.
/// </para>
/// </summary>
internal sealed class SystemPowerStateReader
{
    // WMI is comparatively expensive, so the battery is polled at a lower rate than the sensors.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    private BatteryState _cached = new(true, null);
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private bool _batteryQueryFailed;

    public BatteryState Read()
    {
        if (DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
        {
            return _cached;
        }

        bool onAc = ReadAcLineStatus();
        double? discharge = onAc || _batteryQueryFailed ? null : ReadDischargeWatts();

        _cached = new BatteryState(onAc, discharge);
        _cachedAt = DateTimeOffset.UtcNow;
        return _cached;
    }

    private static bool ReadAcLineStatus()
    {
        if (!NativeMethods.GetSystemPowerStatus(out NativeMethods.SystemPowerStatus status))
        {
            return true;
        }

        // 0 = offline, 1 = online, 255 = unknown. Desktops report 1 or 255.
        return status.AcLineStatus != 0;
    }

    /// <summary>
    /// Reads the discharge rate from the ACPI battery. The value is reported in milliwatts;
    /// some firmwares report milliamps instead, in which case it is converted with the voltage.
    /// </summary>
    private double? ReadDischargeWatts()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            var query = new ObjectQuery("SELECT DischargeRate, Discharging, Voltage FROM BatteryStatus");

            using var searcher = new ManagementObjectSearcher(scope, query);
            double totalMilliwatts = 0;

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    if (item["Discharging"] is bool discharging && !discharging)
                    {
                        continue;
                    }

                    if (item["DischargeRate"] is null)
                    {
                        continue;
                    }

                    totalMilliwatts += Convert.ToDouble(
                        item["DischargeRate"], System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            double watts = totalMilliwatts / 1000.0;
            return watts is > 0.5 and < 500 ? watts : null;
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or COMException)
        {
            // Not every firmware exposes root\WMI BatteryStatus - stop retrying it.
            _batteryQueryFailed = true;
            DiagnosticsLog.Write("WMI BatteryStatus nicht verfügbar", exception);
            return null;
        }
    }
}

/// <summary>Reads the brightness of the internal notebook panel.</summary>
internal sealed class DisplayBrightnessReader
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private double? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private bool _unavailable;

    public double? Read()
    {
        if (_unavailable)
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
        {
            return _cached;
        }

        _cached = ReadCore();
        _cachedAt = DateTimeOffset.UtcNow;
        return _cached;
    }

    private double? ReadCore()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            var query = new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness");

            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    if (item["CurrentBrightness"] is null)
                    {
                        continue;
                    }

                    double percent = Convert.ToDouble(
                        item["CurrentBrightness"], System.Globalization.CultureInfo.InvariantCulture);

                    return Math.Clamp(percent / 100.0, 0, 1);
                }
            }

            _unavailable = true;
            return null;
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or COMException)
        {
            _unavailable = true;
            DiagnosticsLog.Write("WMI WmiMonitorBrightness nicht verfügbar", exception);
            return null;
        }
    }
}

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
