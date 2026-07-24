using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed record ObservedSynapse<TSynapse>(
    TSynapse Synapse,
    NeuronId Subject,
    NeuronId Caller,
    JournalKind Direction,
    long Sequence,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId,
    SynapseId SynapseId)
    where TSynapse : Synapse;
