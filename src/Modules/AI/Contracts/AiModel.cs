namespace DigitalBrain.AI;

/// <summary>
/// One shape for every kind of model — chat, embedding, transcription, image.
/// A model is a compile-time fact: an <see cref="Id"/> on the wire, an
/// <see cref="AiProvider"/> that serves it, and a marker type that identifies it
/// everywhere else.
/// </summary>
public abstract class AiModel
{
    /// <summary>The provider's own identifier, sent on the wire.</summary>
    public abstract string Id { get; }

    public abstract AiProvider Provider { get; }

    /// <summary>
    /// The marker interface for this model. Used as the keyed-service key and as
    /// the name configuration pins a default by.
    /// </summary>
    public abstract Type Marker { get; }

    /// <summary>
    /// Human-facing only — never a lookup key. Defaults to the marker name
    /// without its interface prefix.
    /// </summary>
    public virtual string DisplayName =>
        Marker.Name is ['I', var initial, ..] && char.IsUpper(initial)
            ? Marker.Name[1..]
            : Marker.Name;

    /// <summary>Runs on the owner's machine: no per-token cost, no network.</summary>
    public bool IsLocal => Provider is AiProvider.Ollama or AiProvider.FoundryLocal;
}

// There is deliberately no generic AiModel<TMarker> here. Each kind needs a
// non-generic base to hold its own catalog, and C# has single inheritance, so a
// kind's generic base must derive from that kind — not from a shared generic
// root. Every kind therefore repeats one line to seal Marker against its own
// marker constraint, which is what makes a marker impossible to attach to the
// wrong kind of model.
