using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Runtime;

[ClientEntryPoint]
internal interface IBehaviorWorkerBroker : IGrainWithStringKey
{
    [Alias(nameof(StagePrepare))]
    Task<WorkerOperationReceipt> StagePrepare(
        NeuronId task,
        PrepareTaskOperation command,
        CancellationToken cancellationToken);

    [Alias(nameof(StageTransition))]
    Task<WorkerOperationReceipt> StageTransition(
        NeuronId task,
        TransitionTaskOperation command,
        CancellationToken cancellationToken);

    [Alias(nameof(StageRead))]
    Task<WorkerOperationReceipt> StageRead(
        NeuronId task,
        ReadTaskOperation command,
        CancellationToken cancellationToken);

    [Alias(nameof(StageDispatch))]
    Task<WorkerOperationReceipt> StageDispatch(
        NeuronId task,
        AttemptId attempt,
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken);
}

[GenerateSerializer]
[Alias("behaviors.worker-operation-receipt")]
internal sealed record WorkerOperationReceipt(
    [property: Id(0)] CorrelationId Correlation,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] NeuronId Task);
