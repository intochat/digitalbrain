using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

// Directed wake-up from Execution to the origin that started it (e.g. chat).
// Origin re-Reads the Execution for authority; Result/Failure are a snapshot at
// transition time so transcript can be rebuilt even if re-Read races.
[GenerateSerializer]
[Alias("db.execution.terminal")]
public sealed record ExecutionTerminal(
    [property: Id(0)] NeuronId ExecutionId,
    [property: Id(1)] ExecutionState State,
    [property: Id(2)] long Revision,
    [property: Id(3)] Result? Result = null,
    [property: Id(4)] Failure? Failure = null) : Synapse;
