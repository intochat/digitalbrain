using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The cursor advance of one running <c>[On]</c> reaction (R4-09). The engine opens it around the reaction with the cursor already
/// moved to the signal; every save or publish of the reacting neuron in that flow writes the subscriber's <see cref="SynapseState"/>
/// in the same journal entry, so the reaction's effect and its cursor are durable together or not at all. A reaction that writes
/// nothing leaves <see cref="Written"/> false and the engine writes the cursor alone, as one entry.
/// </summary>
internal sealed class ReactionCursor
{
    private static readonly AsyncLocal<ReactionCursor?> Current = new();
    private readonly GrainId _grainId;
    private readonly Func<byte[]> _encode;

    private ReactionCursor(GrainId grainId, Func<byte[]> encode, string? etag)
    {
        _grainId = grainId;
        _encode = encode;
        ETag = etag;
    }

    /// <summary>The ETag of the subscriber's synapse state the next write expects; moves with each committed entry.</summary>
    public string? ETag { get; private set; }

    /// <summary>True once an entry carried the cursor.</summary>
    public bool Written { get; private set; }

    /// <summary>The open reaction cursor of <paramref name="grainId"/> in this flow, or null.</summary>
    public static ReactionCursor? For(GrainId grainId) => Current.Value is { } open && open._grainId == grainId ? open : null;

    /// <summary>Opens a reaction cursor for this flow; disposing the scope closes it.</summary>
    public static Scope Open(GrainId grainId, Func<byte[]> encode, string? etag)
    {
        var cursor = new ReactionCursor(grainId, encode, etag);
        var previous = Current.Value;
        Current.Value = cursor;
        return new Scope(cursor, previous);
    }

    /// <summary>The subscriber's synapse state, with the advanced cursor, as JSON.</summary>
    public byte[] Encode() => _encode();

    /// <summary>Records that an entry with <paramref name="etag"/> carried the cursor.</summary>
    public void Committed(string etag)
    {
        ETag = etag;
        Written = true;
    }

    /// <summary>Closes the reaction cursor when disposed.</summary>
    public readonly struct Scope(ReactionCursor cursor, ReactionCursor? previous) : IDisposable
    {
        public ReactionCursor Cursor => cursor;

        public void Dispose()
        {
            // Ensure can synchronously catch up another source while a reaction is still running. Its nested
            // writes carry the same synapse record, including the outer cursor, so the outer save must use them.
            if (previous is not null && previous._grainId == cursor._grainId && cursor.Written && cursor.ETag is { } etag)
            {
                previous.Committed(etag);
            }
            Current.Value = previous;
        }
    }
}
