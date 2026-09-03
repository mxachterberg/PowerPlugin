using System.Globalization;

namespace PowerPlugin.Core.Configuration;

/// <summary>
/// Turns the tray timing settings back into a sentence.
/// <para>
/// Refresh rate, averaging window and sampling interval are three independent numbers whose
/// combination is not obvious - "every five seconds the mean of the last five" and "every second
/// the mean of the last five" are both valid and behave very differently. Reading the
/// configuration back in plain language lets the user check what they actually configured, and
/// points out the two combinations that do not do what one might expect.
/// </para>
/// </summary>
public static class TrayDisplayDescription
{
    public static string Describe(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string refresh = FormatDuration(settings.TrayRefreshInterval);

        // The energy and cost modes read straight from the statistics, so neither the sampling
        // rate nor the averaging window has any effect on them.
        if (settings.TrayDisplay != TrayDisplayMode.TotalWatts)
        {
            string what = settings.TrayDisplay == TrayDisplayMode.TodayKilowattHours
                ? "den heutigen Verbrauch in kWh"
                : "die heutigen Stromkosten";

            return $"Das Symbol zeigt {what} und wird alle {refresh} aktualisiert. " +
                   "Messintervall und Mittelung wirken sich auf diese Anzeige nicht aus.";
        }

        var sentences = new List<string>();

        if (settings.TrayValue == TrayValueMode.Instantaneous)
        {
            sentences.Add($"Alle {refresh} wird der zuletzt gemessene Momentanwert angezeigt.");
        }
        else
        {
            TimeSpan window = settings.TrayAverageWindow;
            sentences.Add($"Alle {refresh} wird der Mittelwert der letzten {FormatDuration(window)} angezeigt.");

            double samplesInWindow = window.TotalSeconds / settings.SampleInterval.TotalSeconds;
            if (samplesInWindow >= 1.5)
            {
                sentences.Add($"Das sind rund {samplesInWindow:0} Messwerte pro Anzeige.");
            }
            else
            {
                sentences.Add(
                    $"Das Mittelungsfenster ist kaum länger als das Messintervall " +
                    $"({FormatDuration(settings.SampleInterval)}) - es enthält meist nur einen Messwert, " +
                    "die Anzeige ist also faktisch ungeglättet.");
            }
        }

        if (settings.TrayRefreshInterval < settings.SampleInterval)
        {
            sentences.Add(
                $"Gemessen wird nur alle {FormatDuration(settings.SampleInterval)}; zwischen zwei Messungen " +
                "wiederholt die Anzeige denselben Wert.");
        }

        return string.Join(' ', sentences);
    }

    /// <summary>Formats a duration the way it would be spoken: "500 ms", "1 s", "2,5 s".</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        double seconds = duration.TotalSeconds;

        return seconds < 1
            ? string.Create(CultureInfo.CurrentCulture, $"{duration.TotalMilliseconds:0} ms")
            : string.Create(CultureInfo.CurrentCulture, $"{seconds:0.#} s");
    }
}
