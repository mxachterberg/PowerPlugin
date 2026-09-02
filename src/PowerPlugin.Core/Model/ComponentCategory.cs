namespace PowerPlugin.Core.Model;

/// <summary>
/// Coarse grouping of a power consumer. Used for ordering, colouring and for
/// deciding which estimation model applies to a component.
/// </summary>
public enum ComponentCategory
{
    Cpu,
    Gpu,
    Memory,
    Storage,
    Mainboard,
    Cooling,
    Display,
    PowerSupply,
    Other,
}
