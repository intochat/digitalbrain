namespace DigitalBrain;

/// <summary>Marks a state record with the frozen alias synapses use to address it.</summary>
/// <param name="alias">The state alias.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StateAttribute(string alias) : Attribute
{
    /// <summary>The state alias.</summary>
    public string Alias { get; } = alias;
}
