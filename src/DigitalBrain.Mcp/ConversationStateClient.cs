using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
namespace DigitalBrain.Mcp;

public sealed class ConversationStateClient(IClusterClient cluster, TimeProvider timeProvider)
{
    private const string FeedOutboxKind = "surface-feed";
    public async Task<InoConversationSnapshot> ReadAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default)
    {
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var state = await ReadStateAsync(context, cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }
    public async Task<InoConversationSnapshot> BeginAsync(RuntimeRequestContext context, string commandId, string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 256)
            throw new ArgumentException("A bounded command id is required.", nameof(commandId));
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 16_000)
            throw new ArgumentException("A bounded prompt is required.", nameof(prompt));
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = Conversation(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        var operationId = OperationId(context, commandId);
        var normalizedPrompt = prompt.Trim();
        var inputHash = Hash(normalizedPrompt);
        var acceptedAt = timeProvider.GetUtcNow();
        var grants = context.Grants.Order(StringComparer.Ordinal).ToArray();
        state = await RetryConflictAsync(
            neuron,
            state,
            current =>
            {
                var acceptedProjection = CreateProjection(
                    context,
                    current,
                    operationId,
                    commandId,
                    InoOperationPhase.Accepted,
                    1,
                    acceptedAt,
                    context.CorrelationId,
                    null,
                    false,
                    ToFeedTurns(current).Append(new OperationFeedTurn(commandId, "user", normalizedPrompt, InoConversationStates.Queued)).ToArray());
                return neuron.BeginOperationAsync(
                    current.Revision,
                    commandId,
                    inputHash,
                    operationId,
                    normalizedPrompt,
                    context.CorrelationId,
                    CreateOutbox(operationId, InoOperationPhase.Accepted, 1, acceptedProjection, acceptedAt),
                    acceptedAt,
                    grants);
            },
            cancellationToken).ConfigureAwait(false);
        return ToSnapshot(context, state);
    }
    public async Task<OperationReceipt> DecideApprovalAsync(
        RuntimeRequestContext context,
        string operationId,
        string approvalId,
        bool approved,
        string decisionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(approvalId) || string.IsNullOrWhiteSpace(decisionId))
            throw new ArgumentException("A bounded approval decision identity is required.");
        context = await ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var actor = RequestScope.Id(context);
        var neuron = Conversation(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        if (approved)
            ExternalEffectGrants.Demand(
                state.Operations.FirstOrDefault(operation =>
                    string.Equals(operation.OperationId, operationId, StringComparison.Ordinal))?.Effect?.Kind,
                context.Grants);
        if (TryGetDecisionReceipt(state, operationId, approvalId, approved, decisionId, actor) is { } replay)
            return replay;
        var phase = approved ? InoOperationPhase.Approved : InoOperationPhase.Failed;
        var assistantText = approved
            ? "Approval recorded. INO will apply the approved action."
            : "The requested action was declined. No external action was performed.";
        var now = timeProvider.GetUtcNow();
        state = await RetryConflictAsync(
            neuron,
            state,
            current =>
            {
                if (TryGetDecisionReceipt(current, operationId, approvalId, approved, decisionId, actor) is not null)
                    return Task.FromResult(current);
                var operation = current.Operations.FirstOrDefault(candidate =>
                    string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("The approval operation was not committed.");
                var projection = CreateProjection(
                    context,
                    current,
                    operation.OperationId,
                    operation.CommandId,
                    phase,
                    checked(operation.Version + 1),
                    now,
                    operation.RequestId,
                    approved ? null : assistantText,
                    false,
                    ToFeedTurns(current).Append(new OperationFeedTurn(decisionId, "assistant", assistantText, phase == InoOperationPhase.Approved ? InoConversationStates.Queued : InoConversationStates.Failed)).ToArray(),
                    approvalId: approved ? null : approvalId,
                    effectId: operation.Effect?.EffectId,
                    workflow: operation.Workflow);
                return neuron.DecideApprovalWithAssistantAsync(
                    current.Revision,
                    operation.OperationId,
                    approvalId,
                    approved,
                    decisionId,
                    actor,
                    assistantText,
                    CreateOutbox(operation.OperationId, phase, checked(operation.Version + 1), projection, now),
                    now);
            },
            cancellationToken).ConfigureAwait(false);
        return TryGetDecisionReceipt(state, operationId, approvalId, approved, decisionId, actor)
            ?? throw new RuntimeStateIntegrityException("approval decision was not durably recorded");
    }
    internal Task<RuntimeRequestContext> ResolveContextAsync(RuntimeRequestContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var conversationId = context.ConversationId ?? InoConversationIdentity.From(context);
        DemandConversationId(conversationId);
        return Task.FromResult(context with { ConversationId = conversationId });
    }
    private async Task<ConversationState> ReadStateAsync(RuntimeRequestContext context, CancellationToken cancellationToken)
    {
        var state = await Conversation(context).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return state.Identity is null ? ConversationState.Empty() : state;
    }
    private async Task<ConversationState> EnsureInitializedAsync(RuntimeRequestContext context, IConversationNeuron neuron, CancellationToken cancellationToken)
    {
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var identity = Identity(context);
        if (state.Identity is not null)
        {
            if (state.Identity != identity)
                throw new UnauthorizedAccessException("Conversation identity does not match the authenticated scope.");
            return state;
        }
        try
        {
            return await neuron.InitializeAsync(state.Revision, identity).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeStateConflictException)
        {
            state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.Identity != identity)
                throw new UnauthorizedAccessException("Conversation identity does not match the authenticated scope.");
            return state;
        }
    }
    private static async Task<TResult> RetryConflictAsync<TResult>(IConversationNeuron neuron, ConversationState initial, Func<ConversationState, Task<TResult>> update, CancellationToken cancellationToken)
    {
        var state = initial;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await update(state).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Conversation revision retry exhausted.");
    }
    internal IConversationNeuron Conversation(RuntimeRequestContext context)
    {
        var conversationId = context.ConversationId ?? InoConversationIdentity.From(context);
        DemandConversationId(conversationId);
        return cluster.GetGrain<IConversationNeuron>(RuntimeStateKeys.Conversation(context.OwnerId, context.ActorId, conversationId));
    }
    private static ConversationIdentity Identity(RuntimeRequestContext context)
    {
        var conversationId = context.ConversationId ?? InoConversationIdentity.From(context);
        DemandConversationId(conversationId);
        return new(context.OwnerId, context.ActorId, conversationId);
    }
    internal static string OperationId(RuntimeRequestContext context, string commandId)
    {
        var defaultConversationId = InoConversationIdentity.From(context);
        var conversationScope = context.ConversationId is { } conversationId && !string.Equals(conversationId, defaultConversationId, StringComparison.Ordinal)
            ? "\0" + conversationId
            : string.Empty;
        return "runtime-op-" + Hash(RequestScope.Id(context) + conversationScope + "\0" + commandId);
    }
    internal static void DemandConversationId(string? conversationId)
    {
        if (conversationId is null || conversationId.Length != 68 || !conversationId.StartsWith("ino-", StringComparison.Ordinal))
            throw new ArgumentException("Conversation ids must be canonical scoped identifiers.", nameof(conversationId));
        foreach (var character in conversationId.AsSpan(4))
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new ArgumentException("Conversation ids must be canonical scoped identifiers.", nameof(conversationId));
    }
    private static ConversationOutboxEntry CreateOutbox(string operationId, InoOperationPhase phase, long version, OperationOutboxRecord projection, DateTimeOffset now) => new($"operation:{operationId}:phase:{phase.ToString().ToLowerInvariant()}:v:{version}", FeedOutboxKind, projection.ToPayloadUtf8(), now, null);
    private static OperationReceipt? TryGetDecisionReceipt(ConversationState state, string operationId, string approvalId, bool approved, string decisionId, string actor)
    {
        var operation = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));
        if (operation is null)
            throw new InvalidOperationException("The approval operation was not committed.");
        var approval = operation.Approval ?? throw new InvalidOperationException("The operation has no approval to decide.");
        if (!string.Equals(approval.ApprovalId, approvalId, StringComparison.Ordinal))
            throw new InvalidOperationException("The approval does not belong to this operation.");
        if (approval.DecisionId is null) return null;
        if (!string.Equals(approval.DecisionId, decisionId, StringComparison.Ordinal) ||
            !string.Equals(approval.DecidedBy, actor, StringComparison.Ordinal) ||
            approval.State != (approved ? "approved" : "rejected"))
            throw new InvalidOperationException("An approval decision cannot be changed.");
        return new(operation.OperationId, decisionId, approved ? InoOperationPhase.Approved : InoOperationPhase.Failed, operation.Version);
    }
    private static OperationOutboxRecord CreateProjection(
        RuntimeRequestContext context,
        ConversationState state,
        string operationId,
        string commandId,
        InoOperationPhase phase,
        long version,
        DateTimeOffset occurredAt,
        string requestId,
        string? safeReason,
        bool retryable,
        OperationFeedTurn[] turns,
        ToolAction? action = null,
        string? approvalId = null,
        string? toolId = null,
        string? effectId = null,
        WorkflowReference? workflow = null)
    {
        var identity = state.Identity ?? throw new RuntimeStateIntegrityException("conversation identity is missing");
        var eventId = $"operation:{operationId}:phase:{phase.ToString().ToLowerInvariant()}:v:{version}";
        return OperationOutboxRecord.Create(
            eventId,
            operationId,
            phase,
            version,
            occurredAt,
            conversationId: identity.ConversationId,
            conversationRevision: checked(state.Revision + 1),
            requestId,
            RuntimeStateKeys.Conversation(identity.OwnerId, identity.ActorId, identity.ConversationId),
            new OperationFeedView(commandId, string.Empty, retryable, safeReason, approvalId, action, turns),
            toolId,
            effectId,
            approvalId,
            workflow);
    }
    internal static InoConversationSnapshot ToSnapshot(RuntimeRequestContext context, ConversationState state)
    {
        if (state.Identity is null)
            return new(context.ConversationId ?? InoConversationIdentity.From(context), 0, [], []);
        var projections = state.Outbox.Where(entry => string.Equals(entry.Kind, FeedOutboxKind, StringComparison.Ordinal))
            .Select(TryReadProjection)
            .Where(projection => projection is not null)
            .Cast<OperationOutboxRecord>()
            .GroupBy(projection => projection.OperationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(projection => projection.OperationVersion).Last(),
                StringComparer.Ordinal);
        var operations = state.Operations.Select(operation =>
        {
            projections.TryGetValue(operation.OperationId, out var projection);
            var userText = state.Turns.LastOrDefault(turn =>
                turn.Kind == ConversationTurnKind.User && string.Equals(turn.OperationId, operation.OperationId, StringComparison.Ordinal))?.Text ?? string.Empty;
            var action = operation.Status == ConversationOperationStatus.AwaitingAuthorization &&
                         projection is { Phase: InoOperationPhase.AwaitingAuthorization } currentProjection &&
                         currentProjection.OperationVersion == operation.Version
                ? currentProjection.ToSnapshot().CurrentOperation?.Action
                : null;
            return new InoConversationOperation(
                operation.OperationId,
                operation.CommandId,
                userText,
                LegacyState(operation.Status),
                operation.SafeReason,
                operation.Status == ConversationOperationStatus.RetryScheduled,
                operation.UpdatedAt,
                action,
                null,
                null,
                operation.Version,
                operation.Workflow,
                operation.Status == ConversationOperationStatus.AwaitingApproval ? operation.Approval?.ApprovalId : null,
                Phase: null,
                Capability: operation.Capability,
                Proposal: operation.Proposal);
        }).ToArray();
        var turns = state.Turns.Select(turn => new InoConversationTurn(
            turn.IdempotencyKey,
            turn.Role,
            turn.Text,
            operations.LastOrDefault(operation =>
                string.Equals(operation.CommandId, turn.IdempotencyKey, StringComparison.Ordinal))?.State
            ?? InoConversationStates.Succeeded)).ToArray();
        return new(state.Identity.ConversationId, checked((int)Math.Min(state.Revision, int.MaxValue)), turns, operations);
    }
    private static OperationOutboxRecord? TryReadProjection(ConversationOutboxEntry entry) =>
        OperationOutboxRecord.TryRead(entry.PayloadUtf8, out var projection) ? projection : null;
    private static OperationFeedTurn[] ToFeedTurns(ConversationState state) =>
        state.Turns.Select(turn => new OperationFeedTurn(
            turn.IdempotencyKey,
            turn.Role,
            turn.Text,
            state.Operations.FirstOrDefault(operation =>
                string.Equals(operation.OperationId, turn.OperationId, StringComparison.Ordinal)) is { } operation
                ? LegacyState(operation.Status)
                : InoConversationStates.Succeeded)).ToArray();
    private static string LegacyState(ConversationOperationStatus status) => status switch
    {
        ConversationOperationStatus.Pending => InoConversationStates.Queued,
        ConversationOperationStatus.Running => InoConversationStates.Running,
        ConversationOperationStatus.AwaitingApproval => InoConversationStates.AwaitingApproval,
        ConversationOperationStatus.AwaitingAuthorization => InoConversationStates.AwaitingAuthorization,
        ConversationOperationStatus.RetryScheduled => InoConversationStates.RetryScheduled,
        ConversationOperationStatus.Succeeded => InoConversationStates.Succeeded,
        ConversationOperationStatus.OutcomeUnknown => InoConversationStates.OutcomeUnknown,
        ConversationOperationStatus.Cancelled => InoConversationStates.Cancelled,
        _ => InoConversationStates.Failed
    };
    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
