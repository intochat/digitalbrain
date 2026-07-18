namespace Ino.Core;

/// <summary>
/// Source-generated metadata describing a canonical (INeuron&lt;T&gt;) handler inside
/// a neuron. Populated by the Phase 3 source generator from reflection over
/// INeuron&lt;T&gt; implementations in a neuron assembly.
/// </summary>
public sealed record CanonicalNeuronInfo(
    string SynapseType,
    string GrainType,
    bool IsUserEntry);
