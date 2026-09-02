using System.Collections.Immutable;

namespace PowerPlugin.Core.Model;

/// <summary>
/// The complete power picture of the machine at one instant.
/// </summary>
public sealed class PowerSnapshot
{
    public static readonly PowerSnapshot Empty = new(
        DateTimeOffset.UnixEpoch,
        ImmutableArray<PowerComponent>.Empty,
        belowThresholdWatts: 0,
        conversionLossWatts: 0,
        measuredSystemWatts: null,
        confidence: MeasurementConfidence.Low);

    public PowerSnapshot(
        DateTimeOffset timestamp,
        ImmutableArray<PowerComponent> components,
        double belowThresholdWatts,
        double conversionLossWatts,
        double? measuredSystemWatts,
        MeasurementConfidence confidence)
    {
        Timestamp = timestamp;
        Components = components.IsDefault ? ImmutableArray<PowerComponent>.Empty : components;
        BelowThresholdWatts = belowThresholdWatts;
        ConversionLossWatts = conversionLossWatts;
        MeasuredSystemWatts = measuredSystemWatts;
        Confidence = confidence;
    }

    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Every consumer that draws more than the reporting threshold (1 W by default),
    /// ordered by descending draw. Conversion losses are included as their own entry.
    /// </summary>
    public ImmutableArray<PowerComponent> Components { get; }

    /// <summary>Summed draw of all consumers that stayed below the reporting threshold.</summary>
    public double BelowThresholdWatts { get; }

    /// <summary>Power supply / AC adapter conversion losses contained in <see cref="TotalWatts"/>.</summary>
    public double ConversionLossWatts { get; }

    /// <summary>
    /// Directly measured system power (battery discharge rate or PSU telemetry) when available.
    /// </summary>
    public double? MeasuredSystemWatts { get; }

    public MeasurementConfidence Confidence { get; }

    /// <summary>Total draw at the wall socket in watts, including sub-threshold consumers.</summary>
    public double TotalWatts => Components.Sum(c => c.Watts) + BelowThresholdWatts;

    /// <summary>Total draw of the components themselves, i.e. without conversion losses.</summary>
    public double ComponentWatts => TotalWatts - ConversionLossWatts;

    public double WattsFor(ComponentCategory category) =>
        Components.Where(c => c.Category == category).Sum(c => c.Watts);

    public PowerComponent? Largest => Components.Length == 0 ? null : Components[0];
}
