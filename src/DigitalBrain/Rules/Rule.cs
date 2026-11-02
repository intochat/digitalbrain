namespace DigitalBrain.Rules;

/// <summary>A named invariant over the facts of one operation.</summary>
/// <param name="Name">The stable rule name.</param>
public abstract record Rule(string Name)
{
    /// <summary>True when the invariant does not hold.</summary>
    public abstract bool Broken { get; }

    /// <summary>What a person is told when the rule is broken.</summary>
    public abstract string Message { get; }
}
