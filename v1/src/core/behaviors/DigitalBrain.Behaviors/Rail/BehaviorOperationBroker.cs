using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal interface ITaskOperationClient
{
    ValueTask<TaskOperationSnapshot> PrepareAsync(
        PrepareTaskOperation command,
        CancellationToken cancellationToken);

    ValueTask<ReadTaskOperationResult> ReadAsync(
        ReadTaskOperation command,
        CancellationToken cancellationToken);

    ValueTask<TaskOperationSnapshot> TransitionAsync(
        TransitionTaskOperation command,
        CancellationToken cancellationToken);
}

internal interface IBehaviorOperationDispatcher
{
    ValueTask<ProtectedPayloadReference> DispatchAsync(
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken);
}

internal interface IBehaviorHostBrokerClient : ITaskOperationClient, IBehaviorOperationDispatcher
{
    ValueTask<ProtectedPayloadReference> StorePayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> LoadPayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);

    ValueTask EmitFactAsync(
        BehaviorId behavior,
        string emitAlias,
        ReadOnlyMemory<byte> factJson,
        int hopsRemaining,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> LoadTriggerAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}

internal interface IBehaviorHostBrokerClientFactory
{
    IBehaviorHostBrokerClient Create(OwnerId owner, NeuronId task, AttemptId attempt, NeuronId worker);
}

internal sealed class TaskOwnedOperationHistory
{
    private readonly NeuronId task;
    private readonly AttemptId attempt;
    private readonly ITaskOperationClient client;

    public TaskOwnedOperationHistory(NeuronId task, AttemptId attempt, ITaskOperationClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (task == default || string.IsNullOrWhiteSpace(task.Type) || string.IsNullOrWhiteSpace(task.Name))
        {
            throw new ArgumentException("Task neuron id is required.", nameof(task));
        }

        var taskGrainType = NeuronId.GrainTypeNameOf(typeof(ITask));
        if (!string.Equals(task.Type, taskGrainType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Task neuron grain type must be '{taskGrainType}'.",
                nameof(task));
        }

        if (attempt == default || attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("Attempt id is required.", nameof(attempt));
        }

        this.task = task;
        this.attempt = attempt;
        this.client = client;
    }

    public NeuronId Task => task;

    public AttemptId Attempt => attempt;

    public async ValueTask<BehaviorOperation> PrepareAsync(
        int sequence,
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edge);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await client.PrepareAsync(
            new PrepareTaskOperation(attempt, sequence, ToTaskEdge(edge), requestPayload),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return FromTaskSnapshot(snapshot);
    }

    public async ValueTask<BehaviorOperation?> ReadAsync(
        BehaviorOperationIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RequireIdentity(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await client.ReadAsync(
            new ReadTaskOperation(identity.Attempt, identity.Sequence),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return result.Operation is null ? null : FromTaskSnapshot(result.Operation);
    }

    public async ValueTask<BehaviorOperation> TransitionAsync(
        BehaviorOperationIdentity identity,
        TaskOperationPhase expectedPhase,
        TaskOperationPhase phase,
        ProtectedPayloadReference? responsePayload,
        string? redactedSummary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RequireIdentity(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await client.TransitionAsync(
            new TransitionTaskOperation(
                identity.Attempt,
                identity.Sequence,
                expectedPhase,
                phase,
                responsePayload,
                redactedSummary),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return FromTaskSnapshot(snapshot);
    }

    private void RequireIdentity(BehaviorOperationIdentity identity)
    {
        if (identity.Task != task || identity.Attempt != attempt)
        {
            throw new InvalidOperationException(
                "Operation identity does not match this Task-owned history adapter.");
        }
    }

    private BehaviorOperation FromTaskSnapshot(TaskOperationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new BehaviorOperation(
            new BehaviorOperationIdentity(task, snapshot.Attempt, snapshot.Sequence),
            FromTaskEdge(snapshot.Edge),
            snapshot.RequestPayload,
            snapshot.Phase,
            snapshot.ResponsePayload,
            snapshot.RedactedSummary);
    }

    private static TaskOperationEdge ToTaskEdge(BehaviorCapabilityEdge edge)
        => new(
            edge.Target,
            edge.RequestSynapseId,
            edge.RequestSchemaVersion,
            edge.ResponseSynapseId,
            edge.ResponseSchemaVersion);

    private static BehaviorCapabilityEdge FromTaskEdge(TaskOperationEdge edge)
        => new(
            edge.Target,
            edge.RequestSynapseId,
            edge.RequestSchemaVersion,
            edge.ResponseSynapseId,
            edge.ResponseSchemaVersion);
}

internal sealed class BehaviorOperationBroker
{
    private readonly TaskOwnedOperationHistory history;
    private readonly IReadOnlyList<BehaviorCapabilityEdge> grants;
    private readonly IBehaviorOperationDispatcher dispatcher;
    private int nextSequence;

    public BehaviorOperationBroker(
        TaskOwnedOperationHistory history,
        BehaviorCapabilityEdge grant,
        IBehaviorOperationDispatcher dispatcher)
        : this(history, [grant], dispatcher)
    {
    }

    public BehaviorOperationBroker(
        TaskOwnedOperationHistory history,
        IEnumerable<BehaviorCapabilityEdge> grants,
        IBehaviorOperationDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(dispatcher);

        this.history = history;
        this.grants = [.. grants];
        this.dispatcher = dispatcher;
    }

    public ValueTask<BehaviorOperationResult> ExecuteAsync(
        NeuronId target,
        string requestSynapseId,
        int requestSchemaVersion,
        string responseSynapseId,
        int responseSchemaVersion,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        var edge = new BehaviorCapabilityEdge(
            target,
            requestSynapseId,
            requestSchemaVersion,
            responseSynapseId,
            responseSchemaVersion);
        return ExecuteAsync(edge, requestPayload, cancellationToken);
    }

    public async ValueTask<BehaviorOperationResult> PrepareAsync(
        NeuronId target,
        string requestSynapseId,
        int requestSchemaVersion,
        string responseSynapseId,
        int responseSchemaVersion,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var edge = RequireGranted(
            new BehaviorCapabilityEdge(
                target,
                requestSynapseId,
                requestSchemaVersion,
                responseSynapseId,
                responseSchemaVersion));

        var sequence = ClaimSequence();
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = await history.PrepareAsync(sequence, edge, requestPayload, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ToResult(prepared);
    }

    public async ValueTask<BehaviorOperationResult> MarkDispatchedAsync(
        BehaviorOperationIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var transitioned = await history.TransitionAsync(
            identity,
            TaskOperationPhase.Prepared,
            TaskOperationPhase.Dispatched,
            responsePayload: null,
            redactedSummary: null,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return ToResult(transitioned);
    }

    public async ValueTask<BehaviorOperationResult> RecoverAsync(
        BehaviorOperationIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await history.ReadAsync(identity, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (existing is null)
        {
            throw new InvalidOperationException(
                $"No durable operation exists for sequence {identity.Sequence}.");
        }

        return await ContinueAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BehaviorOperationResult> ExecuteAsync(
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        edge = RequireGranted(edge);

        var sequence = ClaimSequence();
        var identity = new BehaviorOperationIdentity(history.Task, history.Attempt, sequence);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await history.ReadAsync(identity, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (existing is not null)
        {
            if (!EdgesEqual(existing.Edge, edge))
            {
                throw new InvalidOperationException(
                    "Durable operation edge does not match the requested capability edge.");
            }

            return await ContinueAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        var prepared = await history.PrepareAsync(sequence, edge, requestPayload, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await ContinueAsync(prepared, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BehaviorOperationResult> ContinueAsync(
        BehaviorOperation operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (operation.Phase)
        {
            case TaskOperationPhase.Completed:
            case TaskOperationPhase.Uncertain:
                return ToResult(operation);

            case TaskOperationPhase.Dispatched:
                return await MarkUncertainAsync(operation.Identity, cancellationToken)
                    .ConfigureAwait(false);

            case TaskOperationPhase.Prepared:
                return await DispatchPreparedAsync(operation, cancellationToken)
                    .ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unknown operation phase '{operation.Phase}'.");
        }
    }

    private async ValueTask<BehaviorOperationResult> DispatchPreparedAsync(
        BehaviorOperation prepared,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireGranted(prepared.Edge);

        var dispatched = await history.TransitionAsync(
            prepared.Identity,
            TaskOperationPhase.Prepared,
            TaskOperationPhase.Dispatched,
            responsePayload: null,
            redactedSummary: null,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var response = await dispatcher.DispatchAsync(
            dispatched.Edge,
            dispatched.RequestPayload,
            cancellationToken).ConfigureAwait(false);

        if (response.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Dispatcher returned an empty response reference.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var completed = await history.TransitionAsync(
            dispatched.Identity,
            TaskOperationPhase.Dispatched,
            TaskOperationPhase.Completed,
            response,
            redactedSummary: null,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return ToResult(completed);
    }

    private async ValueTask<BehaviorOperationResult> MarkUncertainAsync(
        BehaviorOperationIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uncertain = await history.TransitionAsync(
            identity,
            TaskOperationPhase.Dispatched,
            TaskOperationPhase.Uncertain,
            responsePayload: null,
            redactedSummary: null,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return ToResult(uncertain);
    }

    private int ClaimSequence()
    {
        var sequence = nextSequence;
        nextSequence = checked(sequence + 1);
        return sequence;
    }

    private BehaviorCapabilityEdge RequireGranted(BehaviorCapabilityEdge edge)
    {
        foreach (var grant in grants)
        {
            if (EdgesEqual(grant, edge))
            {
                return grant;
            }
        }

        throw new InvalidOperationException(
            "The requested target/request/response edge is not an exact granted capability.");
    }

    private static bool EdgesEqual(BehaviorCapabilityEdge left, BehaviorCapabilityEdge right)
        => left.Target == right.Target
            && string.Equals(left.RequestSynapseId, right.RequestSynapseId, StringComparison.Ordinal)
            && left.RequestSchemaVersion == right.RequestSchemaVersion
            && string.Equals(left.ResponseSynapseId, right.ResponseSynapseId, StringComparison.Ordinal)
            && left.ResponseSchemaVersion == right.ResponseSchemaVersion;

    private static BehaviorOperationResult ToResult(BehaviorOperation operation)
        => new(operation.Identity, operation.Phase, operation.ResponsePayload);
}
