namespace PowerPlugin.Core.Model;

/// <summary>
/// A single power consumer inside the machine at one point in time.
/// </summary>
/// <param name="Key">Stable identifier, used as the primary key for per-component history.</param>
/// <param name="Name">Human readable name, e.g. "AMD Ryzen 7 5800X".</param>
/// <param name="Category">Grouping used for ordering and colouring.</param>
/// <param name="Watts">Current draw in watts.</param>
/// <param name="Source">How <paramref name="Watts"/> was obtained.</param>
/// <param name="Detail">Optional short explanation shown as a tooltip, e.g. "Package-Sensor, 62 % Last".</param>
public sealed record PowerComponent(
    string Key,
    string Name,
    ComponentCategory Category,
    double Watts,
    PowerReadingSource Source,
    string? Detail = null)
{
    public PowerComponent WithWatts(double watts) => this with { Watts = watts };
}
