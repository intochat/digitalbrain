using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed record ObservedSynapse<TSynapse>(
    TSynapse Synapse,
    SynapseId SynapseId,
    long Sequence,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId,
    SynapseId? CausationId,
    NeuronId Caller,
    JournalKind Direction)
    where TSynapse : Synapse;
