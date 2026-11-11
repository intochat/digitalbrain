namespace DigitalBrain.Units;

/// <summary>The <see cref="ISynapseRestorer"/> of a host that keeps no loaded sources: it restores nothing.</summary>
public sealed class NoSynapseRestorer : ISynapseRestorer
{
    /// <summary>The shared instance.</summary>
    public static NoSynapseRestorer Instance { get; } = new();

    /// <inheritdoc />
    public Task<bool> TryRestore(BrainId brain, string alias, CancellationToken ct) => Task.FromResult(false);
}
