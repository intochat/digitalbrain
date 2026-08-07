using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

internal static class WorkerDispatchRelay
{
    internal const string GrainTypeName = "tasks.worker-dispatch-relay";
}

[GenerateSerializer]
[Alias("tasks.relay-worker-accept")]
[Description("One-shot Task→relay Accept envelope targeting a Worker")]
internal sealed record RelayWorkerAccept(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptRequest Request) : Synapse;

[GenerateSerializer]
[Alias("tasks.relay-worker-continue")]
[Description("One-shot Task→relay Continue envelope targeting a Worker")]
internal sealed record RelayWorkerContinue(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptCursor Cursor) : Synapse;

[GenerateSerializer]
[Alias("tasks.relay-worker-cancel")]
[Description("One-shot Task→relay Cancel envelope targeting a Worker")]
internal sealed record RelayWorkerCancel(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptCursor Cursor) : Synapse;

[GenerateSerializer]
[Alias("tasks.dispatch-worker-accept")]
[Description("Internal relay→Worker Accept dispatch via durable outbox")]
internal sealed record DispatchWorkerAccept(
    [property: Id(0)] AttemptRequest Request) : Synapse;

[GenerateSerializer]
[Alias("tasks.dispatch-worker-continue")]
[Description("Internal relay→Worker Continue dispatch via durable outbox")]
internal sealed record DispatchWorkerContinue(
    [property: Id(0)] AttemptCursor Cursor) : Synapse;

[GenerateSerializer]
[Alias("tasks.dispatch-worker-cancel")]
[Description("Internal relay→Worker Cancel dispatch via durable outbox")]
internal sealed record DispatchWorkerCancel(
    [property: Id(0)] AttemptCursor Cursor) : Synapse;
