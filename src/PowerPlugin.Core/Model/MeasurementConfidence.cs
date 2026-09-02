namespace PowerPlugin.Core.Model;

/// <summary>
/// How much of the reported total is backed by real sensors instead of the model.
/// </summary>
public enum MeasurementConfidence
{
    /// <summary>Almost everything is modelled - no CPU or GPU power sensor was available.</summary>
    Low,

    /// <summary>The dominant consumers are measured, the remainder is modelled.</summary>
    Medium,

    /// <summary>The system total itself is measured (battery discharge or PSU telemetry).</summary>
    High,
}

public static class MeasurementConfidenceExtensions
{
    public static string ToDisplayString(this MeasurementConfidence confidence) => confidence switch
    {
        MeasurementConfidence.High => "Hohe Genauigkeit",
        MeasurementConfidence.Medium => "Mittlere Genauigkeit",
        _ => "Grobe Schätzung",
    };
}
