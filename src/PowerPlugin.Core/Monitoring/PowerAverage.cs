using PowerPlugin.Core.Model;

namespace PowerPlugin.Core.Monitoring;

/// <summary>
/// Smoothing for the displayed value.
/// <para>
/// The instantaneous reading of a PC jumps by tens of watts between two samples, which makes a
/// number in the notification area unreadable. Averaging over a short window calms it down
/// without hiding real load changes.
/// </para>
/// </summary>
public static class PowerAverage
{
    /// <summary>
    /// Time weighted mean power over the last <paramref name="window"/> before
    /// <paramref name="end"/>.
    /// <para>
    /// Every sample is held until the next one arrives, the same zero order hold the energy
    /// recorder uses. Weighting by duration instead of counting samples keeps the result correct
    /// when the sampling interval changes or a sample is delayed - three fast samples must not
    /// outweigh one that covered a whole second.
    /// </para>
    /// </summary>
    /// <param name="ordered">Snapshots in ascending time order.</param>
    /// <param name="end">Upper bound of the window, normally "now".</param>
    /// <param name="window">Length of the averaging window.</param>
    /// <param name="fallbackWatts">Returned when the window contains no usable sample.</param>
    public static double TimeWeightedWatts(
        IEnumerable<PowerSnapshot> ordered,
        DateTimeOffset end,
        TimeSpan window,
        double fallbackWatts = 0)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        if (window <= TimeSpan.Zero)
        {
            return fallbackWatts;
        }

        DateTimeOffset start = end - window;
        double weightedWatts = 0;
        double totalSeconds = 0;
        PowerSnapshot? previous = null;

        foreach (PowerSnapshot snapshot in ordered)
        {
            if (previous is not null)
            {
                Accumulate(previous.Timestamp, snapshot.Timestamp, previous.TotalWatts);
            }

            previous = snapshot;
        }

        if (previous is null)
        {
            return fallbackWatts;
        }

        // The newest sample is held until the end of the window. Without this the most recent
        // value would carry no weight at all.
        Accumulate(previous.Timestamp, end, previous.TotalWatts);

        // Everything is older than the window, or the clock moved backwards: the last known
        // value is still the best answer.
        return totalSeconds > 0 ? weightedWatts / totalSeconds : previous.TotalWatts;

        void Accumulate(DateTimeOffset from, DateTimeOffset to, double watts)
        {
            DateTimeOffset clippedFrom = from > start ? from : start;
            DateTimeOffset clippedTo = to < end ? to : end;

            double seconds = (clippedTo - clippedFrom).TotalSeconds;
            if (seconds <= 0)
            {
                return;
            }

            weightedWatts += watts * seconds;
            totalSeconds += seconds;
        }
    }
}
