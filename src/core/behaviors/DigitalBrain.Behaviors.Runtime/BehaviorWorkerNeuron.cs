using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Behaviors;

[GrainType("worker")]
internal sealed class BehaviorWorkerNeuron :
    Neuron,
    IWorker,
    IBehaviorWorkerBroker,
    IHandle<DispatchWorkerAccept>,
    IHandle<DispatchWorkerContinue>,
    IHandle<DispatchWorkerCancel>
{
    public async Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSelf(request.Worker, request.Task);

        if (request.Goal is not BehaviorActivationGoal)
        {
            throw new NeuronAuthorizationException(
                $"Worker '{Id}' accepts only behavior activations.");
        }

        await SendAsync(
            request.Task,
            new AttemptAccepted(request.Task, request.Worker, request.Attempt, request.Revision));
    }

    public Task Continue(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        RequireSelf(cursor.Worker, cursor.Task);
        return Task.CompletedTask;
    }

    public async Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        RequireSelf(cursor.Worker, cursor.Task);

        await SendAsync(
            cursor.Task,
            new AttemptCancelled(cursor.Task, cursor.Worker, cursor.Attempt, cursor.Revision));
    }

    public Task HandleAsync(DispatchWorkerAccept command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Accept(command.Request);
    }

    public Task HandleAsync(DispatchWorkerContinue command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Continue(command.Cursor);
    }

    public Task HandleAsync(DispatchWorkerCancel command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Cancel(command.Cursor);
    }

    public async Task<WorkerOperationReceipt> StagePrepare(
        NeuronId task,
        PrepareTaskOperation command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        RequireStageTaskIdentity(task);
        cancellationToken.ThrowIfCancellationRequested();

        var delivery = await SendAsync(task, command);
        return new WorkerOperationReceipt(delivery.CorrelationId, Id, task);
    }

    public async Task<WorkerOperationReceipt> StageTransition(
        NeuronId task,
        TransitionTaskOperation command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        RequireStageTaskIdentity(task);
        cancellationToken.ThrowIfCancellationRequested();

        var delivery = await SendAsync(task, command);
        return new WorkerOperationReceipt(delivery.CorrelationId, Id, task);
    }

    public async Task<WorkerOperationReceipt> StageRead(
        NeuronId task,
        ReadTaskOperation command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        RequireStageTaskIdentity(task);
        cancellationToken.ThrowIfCancellationRequested();

        var delivery = await SendAsync(task, command);
        return new WorkerOperationReceipt(delivery.CorrelationId, Id, task);
    }

    public async Task<WorkerOperationReceipt> StageDispatch(
        NeuronId task,
        AttemptId attempt,
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edge);
        cancellationToken.ThrowIfCancellationRequested();
        RequireStageTaskIdentity(task);

        if (attempt == default || attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("invalid-attempt", paramName: null);
        }

        if (requestPayload.Id == Guid.Empty)
        {
            throw new ArgumentException("invalid-protected-reference", paramName: null);
        }

        var authority = GrainFactory.GetGrain<IBehaviorTaskAuthority>(
            BehaviorTaskAuthority.ForOwner(Id.Owner).ToGrainId());
        var snapshot = await authority.ReadValidatedTask(
            task,
            attempt,
            requireActivation: true,
            cancellationToken);
        if (snapshot.Worker != Id)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        var catalog = ServiceProvider.GetRequiredService<ActiveCapabilityCatalog>();
        var typeMap = ServiceProvider.GetRequiredService<ActiveModuleContractTypeMap>();
        var payloads = ServiceProvider.GetRequiredService<IBehaviorProtectedPayloadAccess>();
        var resolved = BehaviorCapabilityEdgeAuthority.ResolveExact(Id.Owner, edge, catalog, typeMap);

        cancellationToken.ThrowIfCancellationRequested();
        var plaintext = await payloads.LoadAsync(Id.Owner, task, attempt, requestPayload, cancellationToken);
        if (plaintext.IsEmpty)
        {
            throw new InvalidOperationException("invalid-payload-content");
        }

        object? materialised;
        try
        {
            materialised = BehaviorPayloadJson.Deserialize(plaintext.Span, resolved.RequestType);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("invalid-request-payload", exception);
        }

        if (materialised is not Synapse requestSynapse)
        {
            throw new InvalidOperationException("invalid-request-payload");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var delivery = await SendAsync(resolved.DeliveryTarget, requestSynapse);
        return new WorkerOperationReceipt(delivery.CorrelationId, Id, task);
    }

    private void RequireStageTaskIdentity(NeuronId task)
    {
        if (task == default || task.Owner != Id.Owner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        if (!string.Equals(
                task.Type,
                NeuronId.GrainTypeNameOf(typeof(ITask)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("invalid-task-identity");
        }
    }

    private void RequireSelf(NeuronId worker, NeuronId task)
    {
        if (worker != Id)
        {
            throw new NeuronAuthorizationException(
                $"Worker '{Id}' cannot act as '{worker}'.");
        }

        if (task.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Worker '{Id}' cannot act on task '{task}' owned by '{task.Owner}'.");
        }
    }
}
