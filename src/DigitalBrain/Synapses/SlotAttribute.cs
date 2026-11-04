namespace DigitalBrain;

/// <summary>Marks a synapse parameter as a capability slot the installer must connect.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class SlotAttribute : Attribute
{
    /// <summary>The slot name; the parameter name when null.</summary>
    public string? Name { get; init; }
}
