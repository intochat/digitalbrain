namespace DigitalBrain.Units;

/// <summary>
/// What a host does when a loaded synapse source becomes live or leaves. The registration is process-wide: a loaded source serves every
/// brain that installs it; <c>brain</c> names the brain whose change caused the call, whose live state neurons are reloaded.
/// </summary>
public interface ISynapseRegistrar
{
    /// <summary>
    /// Makes <paramref name="source"/> live: its states and synapses replace those of the same aliases in the catalog, each alias is hosted
    /// as the state neuron of its new record, its <c>[Signal]</c> types are journalable, and a live state neuron of a replaced alias is
    /// reloaded so its next activation uses the new type.
    /// </summary>
    /// <param name="brain">The brain the source was activated for.</param>
    /// <param name="source">The loaded source.</param>
    /// <param name="ct">Cancellation.</param>
    Task Activate(BrainId brain, LoadedSource source, CancellationToken ct);

    /// <summary>The reverse of <see cref="Activate"/>: removes <paramref name="aliases"/> and the signals only <paramref name="sourceKey"/> declared.</summary>
    /// <param name="brain">The brain the source leaves.</param>
    /// <param name="sourceKey">The key of the loaded source.</param>
    /// <param name="aliases">The state aliases the source declared.</param>
    /// <param name="ct">Cancellation.</param>
    Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct);
}
