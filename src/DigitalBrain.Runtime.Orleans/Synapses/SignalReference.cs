using Orleans;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The only stream item (04 D-01): a reference to a journal position, never signal content. Subscribers pull the signals
/// from the brain-checked journal by their own cursor; a lost or forged reference costs at most one extra read.
/// </summary>
/// <param name="Publisher">The neuron whose journal advanced.</param>
/// <param name="Sequence">The highest appended sequence in this namespace.</param>
[GenerateSerializer, Alias(WireAlias)]
internal sealed record SignalReference([property: Id(0)] NeuronId Publisher, [property: Id(1)] long Sequence)
{
    /// <summary>The frozen wire alias.</summary>
    public const string WireAlias = "synapse.ref";
}
