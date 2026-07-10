using System.Text.Json;

namespace DigitalBrain.Core.V2;

/// <summary>Durable workflow boundary. Every transition is persisted as an aggregate commit; retries are inbox-deduplicated.</summary>
public sealed class V2WorkflowAggregate(IV2AggregateStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<V2AggregateSnapshot> SubmitForApprovalAsync(string aggregateId, string commandId, RequestContext context, CancellationToken cancellationToken = default)
        => await CommitAsync(aggregateId, commandId, context, WorkflowState.AwaitingApproval, null, cancellationToken: cancellationToken);

    public Task<V2AggregateSnapshot> ApproveAsync(string aggregateId, string commandId, RequestContext context, ApprovalRecord approval, OutboxRecord firstEffect, CancellationToken cancellationToken = default)
    {
        if (firstEffect.Ordinal != 0 || string.IsNullOrWhiteSpace(firstEffect.EffectId) || string.IsNullOrWhiteSpace(firstEffect.OperationId))
            throw new ArgumentException("Approval requires the first immutable effect intent at ordinal zero.", nameof(firstEffect));
        if (context.Principal.Kind != PrincipalKind.Operator || context.Principal.Value != approval.Approver.Value)
            throw new UnauthorizedAccessException("Authenticated operator approval is required.");
        return ApproveCoreAsync(aggregateId, commandId, context, approval, firstEffect, cancellationToken);
    }

    private async Task<V2AggregateSnapshot> ApproveCoreAsync(string aggregateId, string commandId, RequestContext context, ApprovalRecord approval, OutboxRecord firstEffect, CancellationToken cancellationToken)
    {
        var current = await ReadStateAsync(aggregateId, cancellationToken);
        if (current.State != WorkflowState.AwaitingApproval) throw new InvalidOperationException("Approval is only legal while awaiting approval.");
        var transitions = current.Transitions.Append(new WorkflowTransition(WorkflowState.AwaitingApproval, WorkflowState.Approved, DateTimeOffset.UtcNow))
            .Append(new WorkflowTransition(WorkflowState.Approved, WorkflowState.ApplyQueued, DateTimeOffset.UtcNow)).ToArray();
        return await CommitAsync(aggregateId, commandId, context, WorkflowState.ApplyQueued, new V2WorkflowPersistedState(WorkflowState.ApplyQueued, approval, transitions), [firstEffect], cancellationToken);
    }

    public Task<V2AggregateSnapshot> RejectAsync(string aggregateId, string commandId, RequestContext context, string reason, CancellationToken cancellationToken = default)
        => CommitTerminalAsync(aggregateId, commandId, context, WorkflowState.Rejected, reason, cancellationToken);

    public async Task<V2AggregateSnapshot> AdvanceAsync(string aggregateId, string commandId, RequestContext context, WorkflowState target, string? reason = null, IReadOnlyList<OutboxRecord>? effects = null, CancellationToken cancellationToken = default)
    {
        var current = await ReadStateAsync(aggregateId, cancellationToken);
        if (!IsAllowed(current.State, target)) throw new InvalidOperationException($"Transition from {current.State} to {target} is not legal.");
        var transitions = current.Transitions.Append(new WorkflowTransition(current.State, target, DateTimeOffset.UtcNow, reason)).ToArray();
        return await CommitAsync(aggregateId, commandId, context, target, new V2WorkflowPersistedState(target, current.Approval, transitions), effects, cancellationToken);
    }

    private static bool IsAllowed(WorkflowState from, WorkflowState to) => (from, to) switch
    {
        (WorkflowState.ApplyQueued, WorkflowState.Applying) => true,
        (WorkflowState.Applying, WorkflowState.RetryScheduled or WorkflowState.Succeeded or WorkflowState.Failed or WorkflowState.OutcomeUnknown) => true,
        (WorkflowState.Failed, WorkflowState.RetryScheduled or WorkflowState.CompensationQueued or WorkflowState.ManualIntervention) => true,
        (WorkflowState.OutcomeUnknown, WorkflowState.CompensationQueued or WorkflowState.ManualIntervention) => true,
        (WorkflowState.CompensationQueued, WorkflowState.Compensated or WorkflowState.ManualIntervention) => true,
        (WorkflowState.RetryScheduled, WorkflowState.Applying or WorkflowState.ManualIntervention) => true,
        _ => false
    };

    private async Task<V2AggregateSnapshot> CommitTerminalAsync(string aggregateId, string commandId, RequestContext context, WorkflowState target, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A safe reason is required.", nameof(reason));
        var current = await ReadStateAsync(aggregateId, cancellationToken);
        if (current.State != WorkflowState.AwaitingApproval) throw new InvalidOperationException($"Transition is not legal from {current.State}.");
        return await CommitAsync(aggregateId, commandId, context, target, new V2WorkflowPersistedState(target, current.Approval, current.Transitions.Append(new WorkflowTransition(current.State, target, DateTimeOffset.UtcNow, reason)).ToArray()), cancellationToken: cancellationToken);
    }

    private async Task<V2AggregateSnapshot> CommitAsync(string aggregateId, string commandId, RequestContext context, WorkflowState target, V2WorkflowPersistedState? supplied, IReadOnlyList<OutboxRecord>? effects = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await store.ReadAsync(aggregateId, cancellationToken);
        var current = supplied ?? new V2WorkflowPersistedState(target, null, [new WorkflowTransition(snapshot.CommitSequence == 0 ? WorkflowState.Proposed : target, target, DateTimeOffset.UtcNow)]);
        var state = JsonSerializer.SerializeToDocument(current, JsonOptions).RootElement.Clone();
        var evt = new V2EventEnvelope($"v2.workflow.{target}", 2, "event-" + Guid.NewGuid().ToString("N"), context.CorrelationId, null, state);
        var result = await store.CommitAsync(aggregateId, new V2CommitRequest(commandId, snapshot.CommitSequence, state, [evt], effects ?? [], DateTimeOffset.UtcNow), cancellationToken);
        return result.Snapshot;
    }

    private async Task<V2WorkflowPersistedState> ReadStateAsync(string aggregateId, CancellationToken cancellationToken)
    {
        var state = (await store.ReadAsync(aggregateId, cancellationToken)).State;
        return state.ValueKind == JsonValueKind.Object ? JsonSerializer.Deserialize<V2WorkflowPersistedState>(state.GetRawText(), JsonOptions) ?? new(WorkflowState.Proposed, null, []) : new(WorkflowState.Proposed, null, []);
    }
}

[GenerateSerializer, Alias("digitalbrain.v2.workflow-persisted-state")]
public sealed record V2WorkflowPersistedState(
    [property: Id(0)] WorkflowState State,
    [property: Id(1)] ApprovalRecord? Approval,
    [property: Id(2)] IReadOnlyList<WorkflowTransition> Transitions);
