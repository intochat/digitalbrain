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
    private const string InProcessClosedOutcome =
        "Hardened execution requires an isolated host/broker; in-process raw execution is closed.";
    private const int FailureReasonMaxLength = 256;

    public async Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSelf(request.Worker, request.Task);

        if (request.Goal is not BehaviorActivationGoal goal)
        {
            throw new NeuronAuthorizationException(
                $"Worker '{Id}' accepts only behavior activations.");
        }

        await SendAsync(
            request.Task,
            new AttemptAccepted(request.Task, request.Worker, request.Attempt, request.Revision));

        var executor = ServiceProvider.GetRequiredService<IBehaviorExecutor>();
        var time = TimeProvider;
        var utcNow = time.GetUtcNow();
        var execution = BehaviorExecutionId.New();
        var capabilities = ToCapabilityEdges(goal.Capabilities);

        BehaviorExecutionOutcome outcome;
        try
        {
            outcome = await executor.ExecuteAsync(
                new BehaviorExecutionRequest(
                    new BehaviorExecutionMetadata(
                        Id.Owner,
                        goal.BehaviorId,
                        goal.Revision,
                        execution),
                    ArtifactBytes: ReadOnlyMemory<byte>.Empty,
                    goal.Revision.Value,
                    request.Task,
                    request.Attempt,
                    goal.TriggerTypeName,
                    goal.ProtectedPayload,
                    capabilities,
                    utcNow),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Never journal exception text — it may include trigger/provider content.
            _ = exception;
            outcome = new BehaviorExecutionOutcome(false, "behavior-execution-exception");
        }

        // In-process executor stays closed until Task 5; leave the attempt Running so reverse-broker
        // operation tests retain an active attempt without hosted product configuration.
        if (!outcome.Succeeded
            && string.Equals(outcome.Outcome, InProcessClosedOutcome, StringComparison.Ordinal))
        {
            return;
        }

        if (outcome.Succeeded)
        {
            await SendAsync(
                request.Task,
                new AttemptSucceeded(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new BehaviorTaskResult(Redact(outcome.Outcome)),
                    Evidence: []));
            return;
        }

        await SendAsync(
            request.Task,
            new AttemptFailed(
                request.Task,
                request.Worker,
                request.Attempt,
                request.Revision,
                new BehaviorTaskFailure("behavior-execution-failed"),
                Retryable: false));
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

    private static BehaviorCapabilityEdge[] ToCapabilityEdges(IReadOnlyList<TaskOperationEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var result = new BehaviorCapabilityEdge[edges.Count];
        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];
            result[index] = new BehaviorCapabilityEdge(
                edge.Target,
                edge.RequestSynapseId,
                edge.RequestSchemaVersion,
                edge.ResponseSynapseId,
                edge.ResponseSchemaVersion);
        }

        return result;
    }

    private static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "execution-failed";
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= FailureReasonMaxLength)
        {
            return trimmed;
        }

        return trimmed[..FailureReasonMaxLength];
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
