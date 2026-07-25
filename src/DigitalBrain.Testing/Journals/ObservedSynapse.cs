using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed record ObservedSynapse<TSynapse>(
    TSynapse Synapse,
    SynapseId SynapseId)
    where TSynapse : Synapse;
