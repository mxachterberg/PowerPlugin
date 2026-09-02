using PowerPlugin.Core.Statistics;

namespace PowerPlugin.App.Ui;

/// <summary>Consistent number formatting for the whole user interface.</summary>
internal static class Formatting
{
    public static string Watts(double watts) => watts switch
    {
        < 10 => $"{watts:0.0} W",
        < 1000 => $"{watts:0} W",
        _ => $"{watts / 1000.0:0.00} kW",
    };

    /// <summary>Compact form for the tray icon, at most three characters wide.</summary>
    public static string TrayWatts(double watts) => watts switch
    {
        < 999.5 => $"{watts:0}",
        < 9950 => $"{watts / 1000.0:0.0}k",
        _ => $"{watts / 1000.0:0}k",
    };

    public static string KilowattHours(double kilowattHours) => kilowattHours switch
    {
        < 0.01 => $"{kilowattHours * 1000:0} Wh",
        < 10 => $"{kilowattHours:0.00} kWh",
        < 1000 => $"{kilowattHours:0.0} kWh",
        _ => $"{kilowattHours:0} kWh",
    };

    public static string Money(decimal amount, string currency) => $"{amount:0.00} {currency}";

    public static string Hours(double hours) => hours < 1
        ? $"{hours * 60:0} min"
        : $"{hours:0.0} h";

    public static string QualityLabel(ForecastQuality quality) => quality switch
    {
        ForecastQuality.High => "Basis: über 30 Tage Messdaten",
        ForecastQuality.Medium => "Basis: über eine Woche Messdaten",
        ForecastQuality.Low => "Basis: wenige Tage - noch ungenau",
        _ => "Basis: weniger als ein Tag - grobe Hochrechnung",
    };

    public static string RelativeDay(DateOnly day)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);

        if (day == today)
        {
            return "Heute";
        }

        return day == today.AddDays(-1) ? "Gestern" : day.ToString("ddd, dd.MM.");
    }
}
