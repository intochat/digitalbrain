namespace Ino.Core;

/// <summary>
/// Source-generated metadata describing a reactive (IReactsTo&lt;T&gt;) handler inside
/// a neuron. Populated by the Phase 3 source generator.
/// </summary>
public sealed record ReactiveNeuronInfo(
    string SynapseType,
    string GrainType);
