namespace DigitalBrain.Runtime.Orleans;

/// <summary>One reason a host will not start. Names keys, never values.</summary>
/// <param name="Key">The configuration key or item at fault.</param>
/// <param name="Message">What to change.</param>
public sealed record TopologyIssue(string Key, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Key}: {Message}";
}
