using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

internal static class WorkerDispatchRelay
{
    internal const string GrainTypeName = "db.execution.worker-dispatch-relay";
}

[GenerateSerializer]
[Alias("db.execution.relay-worker-accept")]
public sealed record RelayWorkerAccept(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptRequest Request) : Synapse;

[GenerateSerializer]
[Alias("db.execution.relay-worker-continue")]
public sealed record RelayWorkerContinue(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptCursor Cursor) : Synapse;

[GenerateSerializer]
[Alias("db.execution.relay-worker-cancel")]
public sealed record RelayWorkerCancel(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptCursor Cursor) : Synapse;

[GenerateSerializer]
[Alias("db.execution.dispatch-worker-accept")]
public sealed record DispatchWorkerAccept(
    [property: Id(0)] AttemptRequest Request) : Synapse;

[GenerateSerializer]
[Alias("db.execution.dispatch-worker-continue")]
public sealed record DispatchWorkerContinue(
    [property: Id(0)] AttemptCursor Cursor) : Synapse;

[GenerateSerializer]
[Alias("db.execution.dispatch-worker-cancel")]
public sealed record DispatchWorkerCancel(
    [property: Id(0)] AttemptCursor Cursor) : Synapse;
