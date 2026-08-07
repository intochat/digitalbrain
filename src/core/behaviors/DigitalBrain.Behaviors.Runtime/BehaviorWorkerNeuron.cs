using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Behaviors.Runtime;

[GrainType("worker")]
internal sealed class BehaviorWorkerNeuron :
    Neuron,
    IWorker,
    IBehaviorWorkerBroker,
    IHandle<DispatchWorkerAccept>,
    IHandle<DispatchWorkerContinue>,
    IHandle<DispatchWorkerCancel>,
    IHandle<CompleteHostedBehaviorExecution>
{
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
            new AttemptAccepted(request.Task, request.Worker, request.Attempt, request.Revision)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        // Process-local CTS for this attempt (never durable). Cancel links into host execution via
        // BehaviorAttemptCancellation; late terminals remain idempotent on the Task.
        BehaviorAttemptCancellation.Rent(request.Task, request.Attempt);

        // Stage hosted work on a one-shot relay so this Worker turn ends before reverse-broker
        // StageDispatch re-enters the same non-reentrant grain. Terminal completion returns via
        // CompleteHostedBehaviorExecution on a later serialized turn.
        var relay = new NeuronId(
            BehaviorExecutionRelay.GrainTypeName,
            Id.Owner,
            Guid.NewGuid().ToString("N"));
        await SendAsync(
            relay,
            new RelayHostedBehaviorExecution(
                Id,
                request,
                BehaviorExecutionId.New(),
                TimeProvider.GetUtcNow())).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        _ = goal;
    }

    public Task Continue(AttemptCursor cursor)
        => Continue(cursor, TurnCancellationToken);

    public async Task Continue(AttemptCursor cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        cancellationToken.ThrowIfCancellationRequested();
        RequireSelf(cursor.Worker, cursor.Task);

        // Fresh worker after user-action park: restage hosted execution through the same durable
        // double-hop relay so this Worker turn ends before reverse-broker callbacks re-enter.
        // Handler/outbox delivery token is threaded explicitly into ReadValidatedTask (not None).
        var authority = GrainFactory.GetGrain<IBehaviorTaskAuthority>(
            BehaviorTaskAuthority.ForOwner(Id.Owner).ToGrainId());
        var snapshot = await authority.ReadValidatedTask(
            cursor.Task,
            cursor.Attempt,
            requireActivation: true,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (snapshot.Worker != Id)
        {
            throw new NeuronAuthorizationException("worker-mismatch");
        }

        if (snapshot.Goal is not BehaviorActivationGoal goal)
        {
            throw new NeuronAuthorizationException(
                $"Worker '{Id}' continues only behavior activations.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var request = new AttemptRequest(
            cursor.Task,
            cursor.Worker,
            cursor.Attempt,
            cursor.Revision,
            goal);
        BehaviorAttemptCancellation.Rent(cursor.Task, cursor.Attempt);
        var relay = new NeuronId(
            BehaviorExecutionRelay.GrainTypeName,
            Id.Owner,
            Guid.NewGuid().ToString("N"));
        await SendAsync(
            relay,
            new RelayHostedBehaviorExecution(
                Id,
                request,
                BehaviorExecutionId.New(),
                TimeProvider.GetUtcNow())).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        RequireSelf(cursor.Worker, cursor.Task);

        // Cooperative: cancel the process-local attempt token before journaling AttemptCancelled.
        BehaviorAttemptCancellation.Cancel(cursor.Task, cursor.Attempt);

        await SendAsync(
            cursor.Task,
            new AttemptCancelled(cursor.Task, cursor.Worker, cursor.Attempt, cursor.Revision)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
        // Pass the Deliver/outbox handler token explicitly into Continue/ReadValidatedTask.
        return Continue(command.Cursor, cancellationToken);
    }

    public Task HandleAsync(DispatchWorkerCancel command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Cancel(command.Cursor);
    }

    public async Task HandleAsync(CompleteHostedBehaviorExecution completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        var request = completion.Attempt;
        RequireSelf(request.Worker, request.Task);
        BehaviorAttemptCancellation.Release(request.Task, request.Attempt);

        if (BehaviorExecutionCodes.IsInProcessClosed(completion.StableCode))
        {
            // Closed residual: leave the attempt Running so reverse-broker operation tests retain an
            // active attempt without requiring a live Behavior Host.
            return;
        }

        var stableCode = BehaviorExecutionCodes.MapHostFailure(completion.StableCode);
        if (completion.Succeeded)
        {
            await SendAsync(
                request.Task,
                new AttemptSucceeded(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new BehaviorTaskResult(BehaviorExecutionCodes.Succeeded),
                    Evidence: [])).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (string.Equals(stableCode, BehaviorExecutionCodes.UserActionRequired, StringComparison.Ordinal))
        {
            if (completion.UserAction is not { } userAction
                || userAction.Task != request.Task
                || userAction.Attempt != request.Attempt)
            {
                await SendAsync(
                    request.Task,
                    new AttemptFailed(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        new BehaviorTaskFailure(BehaviorExecutionCodes.Exception),
                        Retryable: false)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            await SendAsync(request.Task, userAction).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Host-path cancellation (linked attempt CTS or turn cancel) surfaces as Cancelled code.
        // Product Cancel still journals AttemptCancelled via Cancel(); late terminals stay idempotent.
        if (completion.Cancelled
            || string.Equals(stableCode, BehaviorExecutionCodes.Cancelled, StringComparison.Ordinal))
        {
            await SendAsync(
                request.Task,
                new AttemptFailed(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new BehaviorTaskFailure(BehaviorExecutionCodes.Cancelled),
                    Retryable: false)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        await SendAsync(
            request.Task,
            new AttemptFailed(
                request.Task,
                request.Worker,
                request.Attempt,
                request.Revision,
                new BehaviorTaskFailure(stableCode),
                Retryable: false)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        var delivery = await SendAsync(task, command).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        var delivery = await SendAsync(task, command).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        var delivery = await SendAsync(task, command).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (snapshot.Worker != Id)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        var catalog = ServiceProvider.GetRequiredService<ActiveCapabilityCatalog>();
        var typeMap = ServiceProvider.GetRequiredService<ActiveModuleContractTypeMap>();
        var payloads = ServiceProvider.GetRequiredService<IBehaviorProtectedPayloadAccess>();
        var resolved = BehaviorCapabilityEdgeAuthority.ResolveExact(Id.Owner, edge, catalog, typeMap);

        cancellationToken.ThrowIfCancellationRequested();
        var plaintext = await payloads.LoadAsync(Id.Owner, task, attempt, requestPayload, cancellationToken).ConfigureAwait(true);
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

        // Direct awaited Deliver (no outbox) so module-owned authorization failures surface on this
        // turn instead of being swallowed by outbox retry and leaving the host poll stranded.
        var delivery = SynapseDelivery.Create(
            requestSynapse,
            Id,
            sequence: 1,
            cause: null,
            TimeProvider,
            CorrelationId.New());
        try
        {
            await GrainFactory.GetGrain<INeuron>(resolved.DeliveryTarget.ToGrainId()).Deliver(delivery, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (McpAuthorizationRequiredException authorizationRequired)
            when (authorizationRequired.Requirement is { } requirement)
        {
            var lifetime = TimeSpan.FromHours(1);
            // Deterministic epoch from the durable authorization command so StageDispatch redelivery
            // of the same command/task/attempt reproduces the same completer/binding surface.
            var actionEpoch = requirement.CommandId.Value;
            var completer = UserActionCompletionBridge.For(Id.Owner, actionEpoch);
            var custody = ServiceProvider.GetRequiredService<IUserActionCustody>();
            var issued = await ModuleUserActionBoundary.IssueFromAuthorizationRequiredAsync(
                custody,
                Id.Owner,
                task,
                attempt,
                resolved.DeliveryTarget,
                requirement.ServerKey,
                requirement.ServerDisplayName,
                requirement.SignInUrl,
                requirement.State,
                parkRevision: snapshot.Revision,
                lifetime,
                completer,
                actionEpoch,
                cancellationToken).ConfigureAwait(true);

            var bind = new BindUserActionCompletion(
                task,
                attempt,
                resolved.DeliveryTarget,
                issued.Requirement.ModuleId,
                issued.Requirement.ActionReference,
                issued.Requirement.ActionEpoch,
                issued.Requirement.ParkRevision,
                issued.Requirement.ExpiresAt,
                requirement.CommandId,
                requirement.ServerKey,
                requirement.State);
            // Direct Deliver (not outbox): bind must complete before the park exception surfaces.
            await GrainFactory.GetGrain<INeuron>(completer.ToGrainId()).Deliver(
                SynapseDelivery.Create(bind, Id, sequence: 1, cause: null, TimeProvider, CorrelationId.New()),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            var authorization = GrainFactory.GetGrain<IMcpAuthorization>(
                NeuronId.For<IMcpAuthorization>(Id.Owner, McpAuthorizationNeuron.InstanceName).ToGrainId());
            await authorization.BindCompletionTarget(
                new BindMcpAuthorizationCompletionTarget(requirement.CommandId, completer),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            throw new BehaviorUserActionRequiredException(issued.Requirement);
        }

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
