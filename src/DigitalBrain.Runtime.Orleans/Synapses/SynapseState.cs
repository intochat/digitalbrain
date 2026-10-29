namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The synapses a subscriber holds, kept in the subscriber's own grain state under <see cref="Name"/> in the <c>cluster</c> store
/// (JSON, 06 D-14): per source the signal aliases and the cursor, the sequence of the last signal handled (04 D-03, D-04).
/// </summary>
internal sealed class SynapseState
{
    /// <summary>The state name, beside the neuron's own state name.</summary>
    public const string Name = "synapses";

    public List<SynapseSource> Sources { get; set; } = [];
}

/// <summary>One source of a subscriber.</summary>
internal sealed class SynapseSource
{
    /// <summary>The publishing neuron, written <c>brain/type/key</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The <c>[Signal]</c> aliases received from the source.</summary>
    public List<string> Aliases { get; set; } = [];

    /// <summary>The publisher sequence of the last signal handled; references at or below it are duplicates.</summary>
    public long Cursor { get; set; }
}

/// <summary>Counters the engine keeps for diagnostics and facts.</summary>
internal sealed class SynapseCounters
{
    private long _reads;
    private long _duplicates;
    private long _failures;

    /// <summary>Journal reads made by synapse delivery.</summary>
    public long HistoryReads => Interlocked.Read(ref _reads);

    /// <summary>References dropped because they were at or below the cursor.</summary>
    public long DuplicatesDropped => Interlocked.Read(ref _duplicates);

    /// <summary>[On] reactions that threw.</summary>
    public long ReactionFailures => Interlocked.Read(ref _failures);

    internal void Read() => Interlocked.Increment(ref _reads);

    internal void Duplicate() => Interlocked.Increment(ref _duplicates);

    internal void Failure() => Interlocked.Increment(ref _failures);
}
