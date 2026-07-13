using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Kernel.Runtime;

public enum ConversationLifecycle { Uninitialized = 0, Active = 1, Suspended = 2, Completed = 3, Tombstoned = 4 }
public enum ConversationOperationStatus
{
    Pending = 0,
    Running = 1,
    AwaitingAuthorization = 2,
    RetryScheduled = 3,
    Succeeded = 4,
    Failed = 5,
    OutcomeUnknown = 6,
    Cancelled = 7,
    AwaitingApproval = 8
}
public enum ConversationTerminalPolicy { NeverRetry = 0, VerifyBeforeRetry = 1, ManualIntervention = 2 }
public enum ConversationTurnKind { User = 0, Assistant = 1, Authorization = 2, Approval = 3 }

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-identity")]
public sealed record ConversationIdentity(
    [property: Id(0)] TenantId TenantId,
    [property: Id(1)] WorkspaceId WorkspaceId,
    [property: Id(2)] PrincipalRef Principal,
    [property: Id(3)] string ConversationId);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-turn")]
public sealed record ConversationTurn(
    [property: Id(0)] long Sequence,
    [property: Id(1)] string Role,
    [property: Id(2)] string Text,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] string OperationId,
    [property: Id(5)] ConversationTurnKind Kind,
    [property: Id(6)] string IdempotencyKey);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-inbox-entry")]
public sealed record ConversationInboxEntry(
    [property: Id(0)] string CommandId,
    [property: Id(1)] string InputHash,
    [property: Id(2)] string OperationId,
    [property: Id(3)] DateTimeOffset RecordedAt);

[GenerateSerializer, Alias("digitalbrain.runtime.suspended-invocation")]
public sealed record SuspendedInvocation(
    [property: Id(0)] string Provider,
    [property: Id(1)] string ToolId,
    // Retained only to read rolling-deployment state. New transitions always persist an empty value.
    [property: Id(2)] byte[] InputUtf8,
    [property: Id(3)] string AuthorizationAttemptId,
    [property: Id(4)] DateTimeOffset AuthorizationExpiresAt,
    [property: Id(5)] string AuthorizationFlowReference,
    // Retained only to migrate older snapshots; ConversationOperation.Workflow is the sole workflow mapping.
    [property: Id(6)] WorkflowReference? Workflow = null);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-operation")]
public sealed record ConversationOperation(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string CommandId,
    [property: Id(2)] ConversationOperationStatus Status,
    [property: Id(3)] int Attempt,
    [property: Id(4)] DateTimeOffset? NextAttemptAt,
    [property: Id(5)] string? LeaseOwner,
    [property: Id(6)] DateTimeOffset? LeaseExpiresAt,
    [property: Id(7)] ConversationTerminalPolicy TerminalPolicy,
    [property: Id(8)] string? SafeReason,
    [property: Id(9)] SuspendedInvocation? SuspendedInvocation,
    [property: Id(10)] DateTimeOffset UpdatedAt,
    [property: Id(11)] long Version = 0,
    [property: Id(12)] WorkflowReference? Workflow = null,
    [property: Id(13)] string RequestId = "",
    [property: Id(14)] ApprovalRecord? Approval = null,
    [property: Id(15)] EffectRecord? Effect = null);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-outbox-entry")]
public sealed record ConversationOutboxEntry(
    [property: Id(0)] string OutboxId,
    [property: Id(1)] string Kind,
    [property: Id(2)] byte[] PayloadUtf8,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] DateTimeOffset? DispatchedAt)
{
    // Zero is the rolling-deployment representation for entries persisted before durable sequencing.
    [Id(5)] public long Sequence { get; init; }
}

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-archive")]
public sealed record ConversationArchiveDescriptor(
    [property: Id(0)] long ArchivedTurnCount,
    [property: Id(1)] long ThroughSequence,
    [property: Id(2)] DateTimeOffset FirstTurnAt,
    [property: Id(3)] DateTimeOffset LastTurnAt,
    [property: Id(4)] string Digest,
    [property: Id(5)] string HeadSegmentId);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-tombstone")]
public sealed record ConversationTombstone(
    [property: Id(0)] DateTimeOffset DeletedAt,
    [property: Id(1)] string Reason);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-state")]
public sealed record ConversationState(
    [property: Id(0)] int SchemaVersion,
    [property: Id(1)] long Revision,
    [property: Id(2)] ConversationLifecycle Lifecycle,
    [property: Id(3)] ConversationIdentity? Identity,
    [property: Id(4)] ConversationTurn[] Turns,
    [property: Id(5)] ConversationInboxEntry[] Inbox,
    [property: Id(6)] ConversationOperation[] Operations,
    [property: Id(7)] ConversationOutboxEntry[] Outbox,
    [property: Id(8)] ConversationArchiveDescriptor? Archive,
    [property: Id(9)] ConversationTombstone? Tombstone,
    [property: Id(10)] string[] AppliedMigrationIds)
{
    [Id(11)] public AcceptedCommand[] AcceptedCommands { get; init; } = [];
    [Id(12)] public long NextOutboxSequence { get; init; }

    public static ConversationState Empty() => new(
        RuntimeStateSchemas.Conversation,
        0,
        ConversationLifecycle.Uninitialized,
        null,
        [],
        [],
        [],
        [],
        null,
        null,
        []);
}

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-claim")]
public sealed record ConversationClaim(
    [property: Id(0)] ConversationState State,
    [property: Id(1)] ConversationOperation? Operation,
    [property: Id(2)] bool Claimed,
    [property: Id(3)] bool Acquired = false);

/// <summary>Fences a worker completion to the exact lease acquisition that dispatched the work.</summary>
[GenerateSerializer, Alias("digitalbrain.runtime.conversation-lease-fence")]
public sealed record ConversationLeaseFence(
    [property: Id(0)] string LeaseOwner,
    [property: Id(1)] int Attempt);

[Alias("digitalbrain.runtime.i-conversation-neuron")]
public interface IConversationNeuron : IGrainWithStringKey
{
    [Alias("digitalbrain.runtime.conversation.read")]
    Task<ConversationState> ReadAsync();
    [Alias("digitalbrain.runtime.conversation.read-archive")]
    Task<ConversationArchivePage> ReadArchiveAsync(ConversationArchiveCursor? cursor, int maximumTurns);
    [Alias("digitalbrain.runtime.conversation.initialize")]
    Task<ConversationState> InitializeAsync(long expectedRevision, ConversationIdentity identity);
    [Alias("digitalbrain.runtime.conversation.begin-operation")]
    Task<ConversationState> BeginOperationAsync(
        long expectedRevision,
        string commandId,
        string inputHash,
        string operationId,
        string userText,
        string requestId,
        ConversationOutboxEntry acceptedOutbox,
        DateTimeOffset createdAt);
    [Alias("digitalbrain.runtime.conversation.try-claim-operation")]
    Task<ConversationClaim> TryClaimOperationAsync(
        long expectedRevision,
        string operationId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        ConversationOutboxEntry? runningOutbox = null);
    [Alias("digitalbrain.runtime.conversation.try-claim-authorization")]
    Task<ConversationClaim> TryClaimAuthorizationAsync(
        long expectedRevision,
        string operationId,
        string authorizationAttemptId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        ConversationOutboxEntry? runningOutbox = null);
    [Alias("digitalbrain.runtime.conversation.suspend-authorization-with-assistant")]
    Task<ConversationState> SuspendAuthorizationWithAssistantAsync(
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        ConversationLeaseFence? leaseFence = null);
    [Alias("digitalbrain.runtime.conversation.request-approval-with-assistant")]
    Task<ConversationState> RequestApprovalWithAssistantAsync(
        long expectedRevision,
        string operationId,
        ApprovalRecord approval,
        EffectRecord effect,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        WorkflowReference? workflow = null,
        ConversationLeaseFence? leaseFence = null);
    [Alias("digitalbrain.runtime.conversation.decide-approval-with-assistant")]
    Task<ConversationState> DecideApprovalWithAssistantAsync(
        long expectedRevision,
        string operationId,
        string approvalId,
        bool approved,
        string decisionId,
        string decidedBy,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now);
    [Alias("digitalbrain.runtime.conversation.schedule-retry")]
    Task<ConversationState> ScheduleRetryAsync(
        long expectedRevision,
        string operationId,
        DateTimeOffset nextAttemptAt,
        string safeReason,
        DateTimeOffset now,
        ConversationOutboxEntry? retryOutbox = null,
        ConversationLeaseFence? leaseFence = null);
    [Alias("digitalbrain.runtime.conversation.complete-with-assistant")]
    Task<ConversationState> CompleteWithAssistantAsync(
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        WorkflowReference? workflow = null,
        ConversationLeaseFence? leaseFence = null);
    [Alias("digitalbrain.runtime.conversation.complete-effect-with-assistant")]
    Task<ConversationState> CompleteEffectWithAssistantAsync(
        long expectedRevision,
        string operationId,
        EffectRecord effect,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        ConversationLeaseFence? leaseFence = null);
    [Alias("digitalbrain.runtime.conversation.mark-outbox-dispatched")]
    Task<ConversationState> MarkOutboxDispatchedAsync(long expectedRevision, string outboxId, DateTimeOffset dispatchedAt);
    [Alias("digitalbrain.runtime.conversation.record-migration")]
    Task<ConversationState> RecordMigrationAsync(long expectedRevision, string migrationId);
}

/// <summary>
/// The worker has no durable lifecycle state of its own. The conversation grain remains authoritative.
/// </summary>
[Alias("digitalbrain.runtime.i-ino-operation-worker")]
public interface IInoOperationWorkerGrain : IGrainWithStringKey
{
    // This is intentionally the sole interleavable worker call: it only idempotently registers a reminder,
    // so a reminder awaiting conversation state can accept a new durable handoff without an A→B→A cycle.
    [AlwaysInterleave]
    [Alias("digitalbrain.runtime.ino-operation-worker.schedule")]
    Task ScheduleAsync();
}

/// <summary>
/// Serializes delivery of a conversation's durable outbox to its authoritative surface-feed grain.
/// </summary>
[Alias("digitalbrain.runtime.i-ino-conversation-outbox-dispatcher")]
public interface IInoConversationOutboxDispatcherGrain : IGrainWithStringKey
{
    [Alias("digitalbrain.runtime.ino-conversation-outbox-dispatcher.schedule")]
    Task ScheduleAsync();
}

public static class ConversationTransitions
{
    public const int MaximumInlineTurns = 128;
    public const int MaximumInboxEntries = 256;
    public const int MaximumTerminalOperations = 4096;
    public const int MaximumAcceptedCommands = 4096;
    public const int MaximumDispatchedOutboxEntries = 128;
    public const int MaximumPendingOutboxEntries = 512;
    public const int MaximumPendingOutboxPayloadBytes = 1 * 1024 * 1024;
    public const int MaximumMigrationIds = 64;

    public static ConversationState Initialize(ConversationState state, long expectedRevision, ConversationIdentity identity)
    {
        DemandRevision(state, expectedRevision);
        ValidateIdentity(identity);
        if (state.Identity is not null)
        {
            if (state.Identity == identity) return state;
            throw new InvalidOperationException("A conversation grain cannot be rebound to another identity.");
        }
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Active,
            Identity = identity
        });
    }

    public static ConversationState BeginOperation(
        ConversationState state,
        long expectedRevision,
        string commandId,
        string inputHash,
        string operationId,
        string userText,
        string requestId,
        ConversationOutboxEntry acceptedOutbox,
        DateTimeOffset createdAt)
    {
        DemandMutable(state, expectedRevision);
        DemandId(commandId, nameof(commandId));
        DemandId(operationId, nameof(operationId));
        DemandId(requestId, nameof(requestId));
        DemandHash(inputHash, nameof(inputHash));
        ValidateTurnText(userText, nameof(userText));
        ValidatePendingOutbox(acceptedOutbox);
        var priorAccepted = state.AcceptedCommands.FirstOrDefault(command =>
            string.Equals(command.CommandId, commandId, StringComparison.Ordinal));
        if (priorAccepted is not null)
        {
            if (!string.Equals(priorAccepted.OperationId, operationId, StringComparison.Ordinal) ||
                !string.Equals(priorAccepted.InputHash, inputHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A command id cannot be reused with different input or operation identity.");
            var priorOperation = state.Operations.FirstOrDefault(operation =>
                string.Equals(operation.OperationId, operationId, StringComparison.Ordinal));
            if (priorOperation is null || !string.Equals(priorOperation.CommandId, commandId, StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("An accepted command is missing its durable operation.");
            return state;
        }
        var prior = state.Inbox.FirstOrDefault(entry => string.Equals(entry.CommandId, commandId, StringComparison.Ordinal));
        if (prior is not null)
            throw new RuntimeStateIntegrityException("A command inbox record is missing its durable idempotency receipt.");
        var existingOperation = state.Operations.FirstOrDefault(operation =>
            string.Equals(operation.OperationId, operationId, StringComparison.Ordinal));
        if (existingOperation is not null)
            throw new InvalidOperationException("An operation id cannot be rebound to another command.");
        if (state.AcceptedCommands.Length >= MaximumAcceptedCommands)
            throw new InvalidOperationException("The conversation has reached its durable idempotency capacity. Start a new conversation.");
        var operation = new ConversationOperation(
            operationId,
            commandId,
            ConversationOperationStatus.Pending,
            0,
            null,
            null,
            null,
            ConversationTerminalPolicy.VerifyBeforeRetry,
            null,
            null,
            createdAt,
            Version: 1,
            RequestId: requestId);
        var accepted = new AcceptedCommand(
            commandId,
            operationId,
            state.Identity!.ConversationId,
            RequestScope.Id(state.Identity.TenantId, state.Identity.WorkspaceId, state.Identity.Principal),
            commandId,
            inputHash.ToLowerInvariant(),
            requestId,
            createdAt,
            SchemaVersion: 1);
        var next = AppendOutbox(AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.User,
            commandId,
            userText,
            createdAt) with
        {
            Revision = checked(state.Revision + 1),
            Inbox = state.Inbox.Append(new(commandId, inputHash.ToLowerInvariant(), operationId, createdAt)).ToArray(),
            Operations = state.Operations.Append(operation).ToArray(),
            AcceptedCommands = state.AcceptedCommands.Append(accepted).ToArray()
        }, acceptedOutbox);
        return ValidateAndCompact(next);
    }

    public static ConversationClaim TryClaimOperation(
        ConversationState state,
        long expectedRevision,
        string operationId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        ConversationOutboxEntry? runningOutbox = null)
    {
        DemandMutable(state, expectedRevision);
        DemandId(operationId, nameof(operationId));
        DemandId(leaseOwner, nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (runningOutbox is not null) ValidatePendingOutbox(runningOutbox);
        var operation = RequiredOperation(state, operationId);
        if (operation.Status == ConversationOperationStatus.Running && operation.SuspendedInvocation is not null)
            return new(state, operation, false);
        if (operation.LeaseExpiresAt is { } existingLease && existingLease > now &&
            string.Equals(operation.LeaseOwner, leaseOwner, StringComparison.Ordinal))
            return new(state, operation, true, Acquired: false);
        if (IsTerminal(operation.Status) || operation.Status is ConversationOperationStatus.AwaitingApproval or ConversationOperationStatus.AwaitingAuthorization ||
            operation.NextAttemptAt is { } due && due > now ||
            operation.LeaseExpiresAt is { } leaseExpiry && leaseExpiry > now)
            return new(state, operation, false);
        var effect = operation.Effect is { State: "approved" } approvedEffect
            ? approvedEffect with { State = "applying", Version = checked(approvedEffect.Version + 1) }
            : operation.Effect;
        var claimed = operation with
        {
            Status = ConversationOperationStatus.Running,
            Attempt = checked(operation.Attempt + 1),
            NextAttemptAt = null,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = now.Add(leaseDuration),
            Effect = effect,
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        };
        var next = WithPendingOutbox(ReplaceOperation(state, claimed), runningOutbox);
        return new(next, claimed, true, Acquired: true);
    }

    public static ConversationClaim TryClaimAuthorization(
        ConversationState state,
        long expectedRevision,
        string operationId,
        string authorizationAttemptId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        ConversationOutboxEntry? runningOutbox = null)
    {
        DemandMutable(state, expectedRevision);
        DemandId(operationId, nameof(operationId));
        DemandId(authorizationAttemptId, nameof(authorizationAttemptId));
        DemandId(leaseOwner, nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (runningOutbox is not null) ValidatePendingOutbox(runningOutbox);
        var operation = RequiredOperation(state, operationId);
        var invocation = operation.SuspendedInvocation;
        if (operation.Status == ConversationOperationStatus.Running &&
            operation.LeaseExpiresAt is { } activeLease && activeLease > now &&
            string.Equals(operation.LeaseOwner, leaseOwner, StringComparison.Ordinal))
            return new(state, operation, true, Acquired: false);
        if (operation.Status is not (ConversationOperationStatus.AwaitingAuthorization or ConversationOperationStatus.Running) || invocation is null ||
            !string.Equals(invocation.AuthorizationAttemptId, authorizationAttemptId, StringComparison.Ordinal) ||
            operation.LeaseExpiresAt is { } leaseExpiry && leaseExpiry > now)
            return new(state, operation, false);
        var claimed = operation with
        {
            Status = ConversationOperationStatus.Running,
            Attempt = checked(operation.Attempt + 1),
            NextAttemptAt = null,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = now.Add(leaseDuration),
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        };
        var next = WithPendingOutbox(
            ReplaceOperation(state with { Lifecycle = ConversationLifecycle.Active }, claimed),
            runningOutbox);
        return new(next, claimed, true, Acquired: true);
    }

    public static ConversationState SuspendAuthorizationWithAssistant(
        ConversationState state,
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        ConversationLeaseFence? leaseFence = null)
    {
        DemandMutable(state, expectedRevision);
        ValidateInvocation(invocation, now);
        ValidateTurnText(assistantText, nameof(assistantText));
        ValidatePendingOutbox(feedOutbox);
        var operation = RequiredOperation(state, operationId);
        if (IsTerminal(operation.Status)) throw new InvalidOperationException("A terminal operation cannot be suspended.");
        DemandUserTurn(state, operation);
        var priorTurn = FindTurn(
            state,
            operationId,
            ConversationTurnKind.Authorization,
            invocation.AuthorizationAttemptId);
        var priorOutbox = state.Outbox.FirstOrDefault(entry =>
            string.Equals(entry.OutboxId, feedOutbox.OutboxId, StringComparison.Ordinal));
        if (operation.Status == ConversationOperationStatus.AwaitingAuthorization &&
            SameInvocation(operation.SuspendedInvocation, invocation))
        {
            if ((priorTurn is null || string.Equals(priorTurn.Text, assistantText, StringComparison.Ordinal)) &&
                priorOutbox is not null && SameOutbox(priorOutbox, feedOutbox)) return state;
            throw new RuntimeStateIntegrityException("authorization suspension is not atomically complete");
        }
        if (priorTurn is not null || priorOutbox is not null)
            throw new InvalidOperationException("Authorization response identity cannot be reused.");
        DemandLeaseFence(operation, leaseFence, now);
        var suspended = operation with
        {
            Status = ConversationOperationStatus.AwaitingAuthorization,
            SuspendedInvocation = invocation with { InputUtf8 = [], Workflow = null },
            Workflow = invocation.Workflow ?? operation.Workflow,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            NextAttemptAt = invocation.AuthorizationExpiresAt,
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        };
        var next = AppendOutbox(AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Authorization,
            invocation.AuthorizationAttemptId,
            assistantText,
            now) with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Suspended,
            Operations = ReplaceOperationWithoutRevision(state.Operations, suspended)
        }, feedOutbox);
        return ValidateAndCompact(next);
    }

    public static ConversationState RequestApprovalWithAssistant(
        ConversationState state,
        long expectedRevision,
        string operationId,
        ApprovalRecord approval,
        EffectRecord effect,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        WorkflowReference? workflow = null,
        ConversationLeaseFence? leaseFence = null)
    {
        DemandMutable(state, expectedRevision);
        DemandId(operationId, nameof(operationId));
        ValidateApproval(approval, operationId);
        ValidateEffect(effect, operationId, approval.EffectId);
        ValidateTurnText(assistantText, nameof(assistantText));
        ValidatePendingOutbox(feedOutbox);
        var operation = RequiredOperation(state, operationId);
        if (IsTerminal(operation.Status)) throw new InvalidOperationException("A terminal operation cannot request approval.");
        DemandUserTurn(state, operation);
        var priorTurn = FindTurn(state, operationId, ConversationTurnKind.Approval, approval.ApprovalId);
        var priorOutbox = state.Outbox.FirstOrDefault(entry =>
            string.Equals(entry.OutboxId, feedOutbox.OutboxId, StringComparison.Ordinal));
        if (operation.Status == ConversationOperationStatus.AwaitingApproval &&
            operation.Approval == approval && operation.Effect == effect)
        {
            if ((priorTurn is null || string.Equals(priorTurn.Text, assistantText, StringComparison.Ordinal)) &&
                priorOutbox is not null && SameOutbox(priorOutbox, feedOutbox)) return state;
            throw new RuntimeStateIntegrityException("approval request is not atomically complete");
        }
        if (priorTurn is not null || priorOutbox is not null)
            throw new InvalidOperationException("Approval request identity cannot be reused.");
        DemandLeaseFence(operation, leaseFence, now);
        var awaitingApproval = operation with
        {
            Status = ConversationOperationStatus.AwaitingApproval,
            NextAttemptAt = null,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SuspendedInvocation = null,
            Workflow = workflow ?? operation.Workflow,
            Approval = approval,
            Effect = effect,
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        };
        var next = AppendOutbox(AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Approval,
            approval.ApprovalId,
            assistantText,
            now) with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Suspended,
            Operations = ReplaceOperationWithoutRevision(state.Operations, awaitingApproval)
        }, feedOutbox);
        return ValidateAndCompact(next);
    }

    public static ConversationState DecideApprovalWithAssistant(
        ConversationState state,
        long expectedRevision,
        string operationId,
        string approvalId,
        bool approved,
        string decisionId,
        string decidedBy,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        DemandId(operationId, nameof(operationId));
        DemandId(approvalId, nameof(approvalId));
        DemandId(decisionId, nameof(decisionId));
        DemandId(decidedBy, nameof(decidedBy));
        ValidateTurnText(assistantText, nameof(assistantText));
        ValidatePendingOutbox(feedOutbox);

        var operation = RequiredOperation(state, operationId);
        DemandUserTurn(state, operation);
        var approval = operation.Approval ?? throw new InvalidOperationException("The operation has no approval to decide.");
        var effect = operation.Effect ?? throw new RuntimeStateIntegrityException("The approval has no durable effect.");
        var expectedActor = state.Identity is { } identity
            ? RequestScope.Id(identity.TenantId, identity.WorkspaceId, identity.Principal)
            : throw new RuntimeStateIntegrityException("approval decisions require a conversation identity");
        if (!string.Equals(decidedBy, expectedActor, StringComparison.Ordinal))
            throw new InvalidOperationException("An approval decision must be bound to the conversation actor.");
        if (!string.Equals(approval.ApprovalId, approvalId, StringComparison.Ordinal))
            throw new InvalidOperationException("The approval does not belong to this operation.");

        var approvalState = approved ? "approved" : "rejected";
        var effectState = approved ? "approved" : "rejected";
        var priorTurn = FindTurn(state, operationId, ConversationTurnKind.Approval, decisionId);
        var priorOutbox = state.Outbox.FirstOrDefault(entry =>
            string.Equals(entry.OutboxId, feedOutbox.OutboxId, StringComparison.Ordinal));
        if (approval.DecisionId is not null)
        {
            if (string.Equals(approval.DecisionId, decisionId, StringComparison.Ordinal) &&
                string.Equals(approval.DecidedBy, decidedBy, StringComparison.Ordinal) &&
                approval.State == approvalState && effect.State == effectState &&
                priorTurn is not null && string.Equals(priorTurn.Text, assistantText, StringComparison.Ordinal) &&
                priorOutbox is not null && SameOutbox(priorOutbox, feedOutbox))
                return state;
            throw new InvalidOperationException("An approval decision cannot be changed.");
        }
        if (operation.Status != ConversationOperationStatus.AwaitingApproval ||
            approval.State != "requested" || effect.State != "awaiting-approval")
            throw new InvalidOperationException("The approval is not awaiting a decision.");
        if (priorTurn is not null || priorOutbox is not null)
            throw new RuntimeStateIntegrityException("approval decision identity already exists before the decision");

        var decidedApproval = approval with
        {
            State = approvalState,
            Version = checked(approval.Version + 1),
            DecidedAt = now,
            DecidedBy = decidedBy,
            DecisionId = decisionId
        };
        var decidedEffect = effect with
        {
            State = effectState,
            Version = checked(effect.Version + 1)
        };
        var safeReason = approved ? null : "The requested action was declined. No external action was performed.";
        var nextOperation = operation with
        {
            Status = approved ? ConversationOperationStatus.Pending : ConversationOperationStatus.Failed,
            TerminalPolicy = approved ? operation.TerminalPolicy : ConversationTerminalPolicy.NeverRetry,
            SafeReason = safeReason,
            NextAttemptAt = approved ? now : null,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SuspendedInvocation = null,
            Approval = decidedApproval,
            Effect = decidedEffect,
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        };
        var next = AppendOutbox(AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Approval,
            decisionId,
            assistantText,
            now) with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Active,
            Operations = ReplaceOperationWithoutRevision(state.Operations, nextOperation)
        }, feedOutbox);
        return ValidateAndCompact(next);
    }

    public static ConversationState ScheduleRetry(
        ConversationState state,
        long expectedRevision,
        string operationId,
        DateTimeOffset nextAttemptAt,
        string safeReason,
        DateTimeOffset now,
        ConversationOutboxEntry? retryOutbox = null,
        ConversationLeaseFence? leaseFence = null)
    {
        DemandMutable(state, expectedRevision);
        if (nextAttemptAt <= now || string.IsNullOrWhiteSpace(safeReason) || safeReason.Length > 256)
            throw new ArgumentException("Retry scheduling requires a future due time and bounded safe reason.");
        if (retryOutbox is not null) ValidatePendingOutbox(retryOutbox);
        var operation = RequiredOperation(state, operationId);
        if (IsTerminal(operation.Status)) throw new InvalidOperationException("A terminal operation cannot be retried.");
        if (operation.Effect is not null)
            throw new InvalidOperationException("An approved external effect cannot be retried without verification.");
        DemandLeaseFence(operation, leaseFence, now);
        return WithPendingOutbox(ReplaceOperation(state with { Lifecycle = ConversationLifecycle.Active }, operation with
        {
            Status = ConversationOperationStatus.RetryScheduled,
            NextAttemptAt = nextAttemptAt,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SuspendedInvocation = null,
            SafeReason = safeReason,
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        }), retryOutbox);
    }

    public static ConversationState CompleteWithAssistant(
        ConversationState state,
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        WorkflowReference? workflow = null,
        ConversationLeaseFence? leaseFence = null)
    {
        DemandMutable(state, expectedRevision);
        ValidateTerminal(terminalStatus, safeReason);
        ValidateTurnText(assistantText, nameof(assistantText));
        ValidatePendingOutbox(feedOutbox);
        var operation = RequiredOperation(state, operationId);
        if (operation.Effect is not null)
            throw new InvalidOperationException("An approved external effect must complete through the effect transition.");
        DemandUserTurn(state, operation);
        var priorTurn = FindTurn(state, operationId, ConversationTurnKind.Assistant, operationId);
        var priorOutbox = state.Outbox.FirstOrDefault(entry =>
            string.Equals(entry.OutboxId, feedOutbox.OutboxId, StringComparison.Ordinal));
        if (IsTerminal(operation.Status))
        {
            if (operation.Status == terminalStatus && operation.TerminalPolicy == terminalPolicy &&
                string.Equals(operation.SafeReason, safeReason, StringComparison.Ordinal) &&
                (priorTurn is null || string.Equals(priorTurn.Text, assistantText, StringComparison.Ordinal)) &&
                priorOutbox is not null && SameOutbox(priorOutbox, feedOutbox)) return state;
            throw new InvalidOperationException("A terminal operation cannot change its atomic result.");
        }
        DemandLeaseFence(operation, leaseFence, now);
        if (priorTurn is not null || priorOutbox is not null)
            throw new RuntimeStateIntegrityException("terminal response identity already exists before completion");
        var terminal = operation with
        {
            Status = terminalStatus,
            TerminalPolicy = terminalPolicy,
            SafeReason = safeReason,
            NextAttemptAt = null,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SuspendedInvocation = null,
            Workflow = workflow ?? operation.Workflow,
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        };
        var next = AppendOutbox(AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Assistant,
            operationId,
            assistantText,
            now) with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Active,
            Operations = ReplaceOperationWithoutRevision(state.Operations, terminal)
        }, feedOutbox);
        return ValidateAndCompact(next);
    }

    public static ConversationState CompleteEffectWithAssistant(
        ConversationState state,
        long expectedRevision,
        string operationId,
        EffectRecord effect,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        ConversationLeaseFence? leaseFence = null)
    {
        DemandMutable(state, expectedRevision);
        ValidateTerminal(terminalStatus, safeReason);
        ValidateTurnText(assistantText, nameof(assistantText));
        ValidatePendingOutbox(feedOutbox);
        var operation = RequiredOperation(state, operationId);
        var approval = operation.Approval ?? throw new InvalidOperationException("The operation has no approved effect.");
        ValidateEffect(effect, operationId, approval.EffectId);
        DemandUserTurn(state, operation);
        var priorTurn = FindTurn(state, operationId, ConversationTurnKind.Assistant, operationId);
        var priorOutbox = state.Outbox.FirstOrDefault(entry =>
            string.Equals(entry.OutboxId, feedOutbox.OutboxId, StringComparison.Ordinal));
        if (IsTerminal(operation.Status))
        {
            if (operation.Status == terminalStatus && operation.TerminalPolicy == terminalPolicy &&
                string.Equals(operation.SafeReason, safeReason, StringComparison.Ordinal) && operation.Effect == effect &&
                (priorTurn is null || string.Equals(priorTurn.Text, assistantText, StringComparison.Ordinal)) &&
                priorOutbox is not null && SameOutbox(priorOutbox, feedOutbox)) return state;
            throw new InvalidOperationException("A terminal effect cannot change its outcome.");
        }
        DemandLeaseFence(operation, leaseFence, now);
        if (operation.Status != ConversationOperationStatus.Running || operation.Effect?.State != "applying" ||
            operation.Approval.State != "approved" || effect.Version != operation.Effect.Version + 1 ||
            !SameEffectIntent(effect, operation.Effect) ||
            !IsEffectTerminalFor(effect.State, terminalStatus))
            throw new InvalidOperationException("The effect outcome is not valid for the current operation state.");
        if (priorTurn is not null || priorOutbox is not null)
            throw new RuntimeStateIntegrityException("effect response identity already exists before completion");
        var terminal = operation with
        {
            Status = terminalStatus,
            TerminalPolicy = terminalPolicy,
            SafeReason = safeReason,
            NextAttemptAt = null,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SuspendedInvocation = null,
            Effect = effect,
            UpdatedAt = now,
            Version = checked(operation.Version + 1)
        };
        var next = AppendOutbox(AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Assistant,
            operationId,
            assistantText,
            now) with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Active,
            Operations = ReplaceOperationWithoutRevision(state.Operations, terminal)
        }, feedOutbox);
        return ValidateAndCompact(next);
    }

    public static ConversationState MarkOutboxDispatched(
        ConversationState state,
        long expectedRevision,
        string outboxId,
        DateTimeOffset dispatchedAt)
    {
        DemandMutable(state, expectedRevision);
        var entry = state.Outbox.FirstOrDefault(candidate => string.Equals(candidate.OutboxId, outboxId, StringComparison.Ordinal))
                    ?? throw new KeyNotFoundException("Outbox entry not found.");
        if (entry.DispatchedAt is not null) return state;
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            Outbox = state.Outbox.Select(candidate => candidate == entry ? candidate with { DispatchedAt = dispatchedAt } : candidate).ToArray()
        });
    }

    public static ConversationState RecordMigration(ConversationState state, long expectedRevision, string migrationId)
    {
        DemandMutable(state, expectedRevision);
        DemandId(migrationId, nameof(migrationId));
        if (state.AppliedMigrationIds.Contains(migrationId, StringComparer.Ordinal)) return state;
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            AppliedMigrationIds = state.AppliedMigrationIds.Append(migrationId).ToArray()
        });
    }

    public static ConversationState RemoveLegacyAuthorizationPayloads(ConversationState state)
    {
        var changed = false;
        var operations = state.Operations.Select(operation =>
        {
            if (operation.SuspendedInvocation is not { } invocation ||
                (invocation.InputUtf8.Length == 0 && invocation.Workflow is null))
                return operation;
            changed = true;
            return operation with
            {
                Workflow = operation.Workflow ?? invocation.Workflow,
                SuspendedInvocation = invocation with { InputUtf8 = [], Workflow = null }
            };
        }).ToArray();
        if (!changed) return state;
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            Operations = operations
        });
    }

    public static ConversationState MigrateLegacyOutboxSequences(ConversationState state)
    {
        if (state.Outbox.Length == 0 || state.Outbox.All(entry => entry.Sequence > 0)) return state;
        if (state.Outbox.Any(entry => entry.Sequence > 0) || state.NextOutboxSequence != 0)
            throw new RuntimeStateIntegrityException("legacy conversation outbox sequence state is mixed");
        var sequence = 0L;
        var outbox = state.Outbox
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.OutboxId, StringComparer.Ordinal)
            .Select(entry => entry with { Sequence = checked(++sequence) })
            .ToArray();
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            Outbox = outbox,
            NextOutboxSequence = sequence
        });
    }

    public static ConversationState MigrateLegacyAcceptedCommands(ConversationState state)
    {
        if (state.Inbox.Length == 0) return state;
        var identity = state.Identity ?? throw new RuntimeStateIntegrityException(
            "legacy accepted-command migration requires a conversation identity");
        var acceptedByCommand = state.AcceptedCommands.ToDictionary(command => command.CommandId, StringComparer.Ordinal);
        var operations = state.Operations.ToArray();
        var migrated = new List<AcceptedCommand>();
        var changed = false;

        foreach (var inbox in state.Inbox.OrderBy(entry => entry.RecordedAt).ThenBy(entry => entry.CommandId, StringComparer.Ordinal))
        {
            var operationIndex = Array.FindIndex(operations, operation =>
                string.Equals(operation.OperationId, inbox.OperationId, StringComparison.Ordinal));
            if (operationIndex < 0 || !string.Equals(operations[operationIndex].CommandId, inbox.CommandId, StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("legacy inbox entry is not atomically linked to an operation");
            if (acceptedByCommand.TryGetValue(inbox.CommandId, out var existing))
            {
                if (!string.Equals(existing.OperationId, inbox.OperationId, StringComparison.Ordinal) ||
                    !string.Equals(existing.InputHash, inbox.InputHash, StringComparison.OrdinalIgnoreCase))
                    throw new RuntimeStateIntegrityException("legacy accepted-command metadata conflicts with its inbox entry");
                continue;
            }

            var operation = operations[operationIndex];
            var requestId = operation.RequestId;
            if (string.IsNullOrWhiteSpace(requestId))
            {
                requestId = inbox.CommandId;
                operations[operationIndex] = operation with { RequestId = requestId };
                changed = true;
            }
            else
            {
                DemandId(requestId, nameof(operation.RequestId));
            }

            var accepted = new AcceptedCommand(
                inbox.CommandId,
                inbox.OperationId,
                identity.ConversationId,
                RequestScope.Id(identity.TenantId, identity.WorkspaceId, identity.Principal),
                inbox.CommandId,
                inbox.InputHash.ToLowerInvariant(),
                requestId,
                inbox.RecordedAt,
                SchemaVersion: 1);
            acceptedByCommand.Add(accepted.CommandId, accepted);
            migrated.Add(accepted);
        }

        if (!changed && migrated.Count == 0) return state;
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            Operations = operations,
            AcceptedCommands = state.AcceptedCommands.Concat(migrated).ToArray()
        });
    }

    public static void Validate(ConversationState state)
    {
        if (state.SchemaVersion != RuntimeStateSchemas.Conversation || state.Revision < 0 || state.NextOutboxSequence < 0 ||
            !Enum.IsDefined(state.Lifecycle) || state.Turns is null || state.Inbox is null ||
            state.Operations is null || state.Outbox is null || state.AppliedMigrationIds is null ||
            state.AcceptedCommands is null)
            throw new RuntimeStateIntegrityException("invalid conversation schema");
        if (state.Revision == 0 && state.Identity is not null || state.Revision > 0 && state.Identity is null)
            throw new RuntimeStateIntegrityException("invalid conversation identity lifecycle");
        if (state.Identity is not null) ValidateIdentity(state.Identity);
        var pendingOutbox = state.Outbox.Where(entry => entry.DispatchedAt is null).ToArray();
        if (state.Turns.Length > MaximumInlineTurns || state.Inbox.Length > MaximumInboxEntries ||
            state.AppliedMigrationIds.Length > MaximumMigrationIds ||
            state.Operations.Count(operation => IsTerminal(operation.Status)) > MaximumTerminalOperations ||
            state.AcceptedCommands.Length > MaximumAcceptedCommands ||
            state.Outbox.Count(entry => entry.DispatchedAt is not null) > MaximumDispatchedOutboxEntries ||
            pendingOutbox.Length > MaximumPendingOutboxEntries ||
            pendingOutbox.Sum(entry => (long)(entry.PayloadUtf8?.Length ?? 0)) > MaximumPendingOutboxPayloadBytes)
            throw new RuntimeStateIntegrityException("conversation retention bound exceeded");
        ConversationArchiveTransitions.ValidateDescriptor(state.Archive, state.Turns);
        if (state.Turns.Select(turn => (turn.OperationId, turn.Kind, turn.IdempotencyKey)).Distinct().Count() !=
            state.Turns.Length)
            throw new RuntimeStateIntegrityException("duplicate conversation turn idempotency identity");
        for (var index = 0; index < state.Turns.Length; index++)
        {
            ValidateTurn(state.Turns[index]);
            if (index > 0 && state.Turns[index - 1].Sequence >= state.Turns[index].Sequence)
                throw new RuntimeStateIntegrityException("conversation turn sequence is not monotonic");
        }
        var actorScope = state.Identity is { } identity
            ? RequestScope.Id(identity.TenantId, identity.WorkspaceId, identity.Principal)
            : null;
        foreach (var operation in state.Operations)
        {
            ValidateOperation(operation);
            if (operation.Approval?.DecidedBy is { } decidedBy &&
                !string.Equals(decidedBy, actorScope, StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("approval decision actor does not match conversation identity");
        }
        foreach (var entry in state.Outbox) ValidateOutbox(entry);
        var sequencedOutbox = state.Outbox.Where(entry => entry.Sequence > 0).ToArray();
        if (sequencedOutbox.Length > 0 && sequencedOutbox.Length != state.Outbox.Length)
            throw new RuntimeStateIntegrityException("legacy conversation outbox sequence state is mixed");
        if (sequencedOutbox.Select(entry => entry.Sequence).Distinct().Count() != sequencedOutbox.Length ||
            sequencedOutbox.Any(entry => entry.Sequence > state.NextOutboxSequence) ||
            sequencedOutbox.Length == 0 && state.Outbox.Length > 0 && state.NextOutboxSequence != 0)
            throw new RuntimeStateIntegrityException("invalid conversation outbox sequence");
        ValidateAcceptedCommands(state);
    }

    private static ConversationState ReplaceOperation(ConversationState state, ConversationOperation operation)
    {
        var next = state with
        {
            Revision = checked(state.Revision + 1),
            Operations = state.Operations.Select(candidate =>
                string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal) ? operation : candidate).ToArray()
        };
        return ValidateAndCompact(next);
    }

    private static ConversationOperation[] ReplaceOperationWithoutRevision(
        ConversationOperation[] operations,
        ConversationOperation operation) =>
        operations.Select(candidate => string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal)
            ? operation
            : candidate).ToArray();

    private static ConversationState AppendTurnRecord(
        ConversationState state,
        string operationId,
        ConversationTurnKind kind,
        string idempotencyKey,
        string text,
        DateTimeOffset createdAt)
    {
        var sequence = state.Turns.Length == 0
            ? (state.Archive?.ThroughSequence ?? 0) + 1
            : checked(state.Turns[^1].Sequence + 1);
        var role = kind == ConversationTurnKind.User ? "user" : "assistant";
        return state with
        {
            Turns = state.Turns.Append(new(
                sequence,
                role,
                text,
                createdAt,
                operationId,
                kind,
                idempotencyKey)).ToArray()
        };
    }

    private static ConversationTurn? FindTurn(
        ConversationState state,
        string operationId,
        ConversationTurnKind kind,
        string idempotencyKey) =>
        state.Turns.FirstOrDefault(turn =>
            string.Equals(turn.OperationId, operationId, StringComparison.Ordinal) && turn.Kind == kind &&
            string.Equals(turn.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    private static void DemandUserTurn(ConversationState state, ConversationOperation operation)
    {
        if (state.Turns.Any(turn => turn.Kind == ConversationTurnKind.User &&
                string.Equals(turn.OperationId, operation.OperationId, StringComparison.Ordinal)) ||
            state.Inbox.Any(entry => string.Equals(entry.CommandId, operation.CommandId, StringComparison.Ordinal) &&
                string.Equals(entry.OperationId, operation.OperationId, StringComparison.Ordinal)) || state.Archive is not null)
            return;
        throw new RuntimeStateIntegrityException("operation has no ordered user turn");
    }

    private static bool SameInvocation(SuspendedInvocation? first, SuspendedInvocation second) =>
        first is not null && string.Equals(first.Provider, second.Provider, StringComparison.Ordinal) &&
        string.Equals(first.ToolId, second.ToolId, StringComparison.Ordinal) &&
        string.Equals(first.AuthorizationAttemptId, second.AuthorizationAttemptId, StringComparison.Ordinal) &&
        string.Equals(first.AuthorizationFlowReference, second.AuthorizationFlowReference, StringComparison.Ordinal) &&
        first.AuthorizationExpiresAt == second.AuthorizationExpiresAt;

    private static bool SameOutbox(ConversationOutboxEntry first, ConversationOutboxEntry second) =>
        string.Equals(first.OutboxId, second.OutboxId, StringComparison.Ordinal) &&
        string.Equals(first.Kind, second.Kind, StringComparison.Ordinal) && first.CreatedAt == second.CreatedAt &&
        first.PayloadUtf8.AsSpan().SequenceEqual(second.PayloadUtf8);

    private static bool SameEffectIntent(EffectRecord first, EffectRecord second) =>
        string.Equals(first.EffectId, second.EffectId, StringComparison.Ordinal) &&
        string.Equals(first.OperationId, second.OperationId, StringComparison.Ordinal) &&
        string.Equals(first.Kind, second.Kind, StringComparison.Ordinal) &&
        string.Equals(first.Scope, second.Scope, StringComparison.Ordinal) &&
        string.Equals(first.ProviderIdempotencyKey, second.ProviderIdempotencyKey, StringComparison.Ordinal);

    private static ConversationState AppendOutbox(ConversationState state, ConversationOutboxEntry entry)
    {
        var pendingOutbox = state.Outbox.Where(candidate => candidate.DispatchedAt is null).ToArray();
        if (pendingOutbox.Length >= MaximumPendingOutboxEntries ||
            pendingOutbox.Sum(candidate => (long)(candidate.PayloadUtf8?.Length ?? 0)) + entry.PayloadUtf8.Length >
                MaximumPendingOutboxPayloadBytes)
            throw new InvalidOperationException("The conversation outbox is at its bounded delivery capacity.");
        var sequence = checked(state.NextOutboxSequence + 1);
        return state with
        {
            Outbox = state.Outbox.Append(entry with
            {
                PayloadUtf8 = entry.PayloadUtf8.ToArray(),
                Sequence = sequence
            }).ToArray(),
            NextOutboxSequence = sequence
        };
    }

    private static ConversationState WithPendingOutbox(
        ConversationState state,
        ConversationOutboxEntry? entry,
        bool incrementRevision = false)
    {
        if (entry is null) return state;
        var existing = state.Outbox.FirstOrDefault(candidate =>
            string.Equals(candidate.OutboxId, entry.OutboxId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (SameOutbox(existing, entry)) return state;
            throw new InvalidOperationException("An outbox identity cannot be rebound to different payload.");
        }
        return ValidateAndCompact(AppendOutbox(state with
        {
            Revision = incrementRevision ? checked(state.Revision + 1) : state.Revision
        }, entry));
    }

    private static void ValidatePendingOutbox(ConversationOutboxEntry entry)
    {
        ValidateOutbox(entry);
        if (entry.DispatchedAt is not null)
            throw new ArgumentException("Atomic conversation responses must enqueue an undispatched outbox entry.", nameof(entry));
    }

    private static void ValidateTerminal(ConversationOperationStatus terminalStatus, string? safeReason)
    {
        if (!IsTerminal(terminalStatus))
            throw new ArgumentException("The requested operation state is not terminal.", nameof(terminalStatus));
        if (safeReason is { Length: > 256 })
            throw new ArgumentException("Safe reasons must be bounded.", nameof(safeReason));
    }

    private static void ValidateTurn(ConversationTurn turn)
    {
        if (turn.Sequence < 1 || !Enum.IsDefined(turn.Kind))
            throw new RuntimeStateIntegrityException("invalid conversation turn metadata");
        DemandId(turn.OperationId, nameof(turn.OperationId));
        DemandId(turn.IdempotencyKey, nameof(turn.IdempotencyKey));
        ValidateTurnText(turn.Text, nameof(turn.Text));
        if (turn.Role != (turn.Kind == ConversationTurnKind.User ? "user" : "assistant"))
            throw new RuntimeStateIntegrityException("conversation turn role does not match its kind");
    }

    private static void ValidateTurnText(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 16_000)
            throw new ArgumentException("Conversation turns require bounded text.", name);
    }

    private static ConversationState ValidateAndCompact(ConversationState state)
    {
        state = Compact(state);
        Validate(state);
        return state;
    }

    private static ConversationState Compact(ConversationState state)
    {
        state = ConversationArchiveTransitions.Compact(state, MaximumInlineTurns);
        var active = state.Operations.Where(operation => !IsTerminal(operation.Status));
        var terminal = state.Operations.Where(operation => IsTerminal(operation.Status));
        var pendingOutbox = state.Outbox.Where(entry => entry.DispatchedAt is null);
        var dispatchedOutbox = state.Outbox.Where(entry => entry.DispatchedAt is not null)
            .OrderBy(entry => entry.DispatchedAt).TakeLast(MaximumDispatchedOutboxEntries);
        var inbox = state.Inbox.OrderBy(entry => entry.RecordedAt).TakeLast(MaximumInboxEntries).ToArray();
        var acceptedCommands = state.AcceptedCommands.OrderBy(command => command.AcceptedAt).ToArray();
        return state with
        {
            Inbox = inbox,
            AcceptedCommands = acceptedCommands,
            Operations = active.Concat(terminal).OrderBy(operation => operation.UpdatedAt).ToArray(),
            Outbox = pendingOutbox.Concat(dispatchedOutbox)
                .OrderBy(entry => entry.Sequence == 0 ? 0 : 1)
                .ThenBy(entry => entry.Sequence)
                .ThenBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.OutboxId, StringComparer.Ordinal)
                .ToArray(),
            AppliedMigrationIds = state.AppliedMigrationIds.TakeLast(MaximumMigrationIds).ToArray()
        };
    }

    private static ConversationOperation RequiredOperation(ConversationState state, string operationId) =>
        state.Operations.FirstOrDefault(operation => string.Equals(operation.OperationId, operationId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException("Conversation operation not found.");

    private static bool IsTerminal(ConversationOperationStatus status) => status is
        ConversationOperationStatus.Succeeded or ConversationOperationStatus.Failed or
        ConversationOperationStatus.OutcomeUnknown or ConversationOperationStatus.Cancelled;

    private static bool IsEffectTerminalFor(string effectState, ConversationOperationStatus status) =>
        status switch
        {
            ConversationOperationStatus.Succeeded => effectState == "succeeded",
            ConversationOperationStatus.Failed => effectState == "failed",
            ConversationOperationStatus.OutcomeUnknown => effectState == "outcome-unknown",
            _ => false
        };

    private static void DemandMutable(ConversationState state, long expectedRevision)
    {
        DemandRevision(state, expectedRevision);
        if (state.Identity is null || state.Lifecycle is ConversationLifecycle.Uninitialized or ConversationLifecycle.Tombstoned)
            throw new InvalidOperationException("Conversation state is not mutable.");
    }

    private static void DemandLeaseFence(
        ConversationOperation operation,
        ConversationLeaseFence? leaseFence,
        DateTimeOffset now)
    {
        if (leaseFence is null)
            throw new InvalidOperationException("A worker lease fence is required.");
        DemandId(leaseFence.LeaseOwner, nameof(leaseFence.LeaseOwner));
        if (leaseFence.Attempt < 1 || operation.Status != ConversationOperationStatus.Running ||
            !string.Equals(operation.LeaseOwner, leaseFence.LeaseOwner, StringComparison.Ordinal) ||
            operation.Attempt != leaseFence.Attempt || operation.LeaseExpiresAt is not { } expiry || expiry <= now)
            throw new RuntimeStateConflictException(leaseFence.Attempt, operation.Attempt);
    }

    private static void DemandRevision(ConversationState state, long expectedRevision)
    {
        if (state.Revision != expectedRevision) throw new RuntimeStateConflictException(expectedRevision, state.Revision);
    }

    private static void ValidateIdentity(ConversationIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.TenantId.Value) || string.IsNullOrWhiteSpace(identity.WorkspaceId.Value) ||
            string.IsNullOrWhiteSpace(identity.Principal.Value) || string.IsNullOrWhiteSpace(identity.ConversationId) ||
            identity.ConversationId.Length > 256)
            throw new ArgumentException("A complete bounded conversation identity is required.", nameof(identity));
    }

    private static void ValidateAcceptedCommands(ConversationState state)
    {
        if (state.AcceptedCommands.Select(command => command.CommandId).Distinct(StringComparer.Ordinal).Count() !=
            state.AcceptedCommands.Length)
            throw new RuntimeStateIntegrityException("duplicate accepted command identity");
        foreach (var command in state.AcceptedCommands)
        {
            DemandId(command.CommandId, nameof(command.CommandId));
            DemandId(command.OperationId, nameof(command.OperationId));
            DemandId(command.ConversationId, nameof(command.ConversationId));
            DemandId(command.ActorScope, nameof(command.ActorScope));
            DemandId(command.IdempotencyKey, nameof(command.IdempotencyKey));
            DemandId(command.RequestId, nameof(command.RequestId));
            DemandHash(command.InputHash, nameof(command.InputHash));
            if (command.SchemaVersion != 1 || command.AcceptedAt == default ||
                !string.Equals(command.CommandId, command.IdempotencyKey, StringComparison.Ordinal) ||
                state.Identity is null || !string.Equals(command.ConversationId, state.Identity.ConversationId, StringComparison.Ordinal) ||
                !string.Equals(command.ActorScope, RequestScope.Id(state.Identity.TenantId, state.Identity.WorkspaceId, state.Identity.Principal), StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("accepted command metadata is invalid");
            var operation = state.Operations.FirstOrDefault(candidate =>
                string.Equals(candidate.OperationId, command.OperationId, StringComparison.Ordinal));
            if (operation is null || !string.Equals(operation.CommandId, command.CommandId, StringComparison.Ordinal) ||
                !string.Equals(operation.RequestId, command.RequestId, StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("accepted command is not atomically linked to its operation");
            var inbox = state.Inbox.FirstOrDefault(entry =>
                string.Equals(entry.CommandId, command.CommandId, StringComparison.Ordinal));
            if (inbox is not null &&
                (!string.Equals(inbox.OperationId, command.OperationId, StringComparison.Ordinal) ||
                 !string.Equals(inbox.InputHash, command.InputHash, StringComparison.OrdinalIgnoreCase)))
                throw new RuntimeStateIntegrityException("accepted command inbox metadata is invalid");
        }
    }

    private static void ValidateApproval(ApprovalRecord approval, string operationId)
    {
        DemandId(approval.ApprovalId, nameof(approval.ApprovalId));
        DemandId(approval.OperationId, nameof(approval.OperationId));
        DemandId(approval.EffectId, nameof(approval.EffectId));
        if (!string.Equals(approval.OperationId, operationId, StringComparison.Ordinal) ||
            approval.State is not "requested" and not "approved" and not "rejected" ||
            approval.Version < 1 || approval.RequestedAt == default ||
            approval.DecidedBy is { Length: > 256 } || approval.DecisionId is { Length: > 256 } ||
            approval.State == "requested" && (approval.DecidedAt is not null || approval.DecidedBy is not null || approval.DecisionId is not null) ||
            (approval.State is "approved" or "rejected") &&
            (approval.DecidedAt is null || string.IsNullOrWhiteSpace(approval.DecidedBy) || string.IsNullOrWhiteSpace(approval.DecisionId)))
            throw new ArgumentException("Approval metadata is invalid.", nameof(approval));
    }

    private static void ValidateEffect(EffectRecord effect, string operationId, string approvalEffectId)
    {
        DemandId(effect.EffectId, nameof(effect.EffectId));
        DemandId(effect.OperationId, nameof(effect.OperationId));
        DemandId(effect.Kind, nameof(effect.Kind));
        DemandId(effect.Scope, nameof(effect.Scope));
        DemandId(effect.ProviderIdempotencyKey, nameof(effect.ProviderIdempotencyKey));
        if (!string.Equals(effect.OperationId, operationId, StringComparison.Ordinal) ||
            !string.Equals(effect.EffectId, approvalEffectId, StringComparison.Ordinal) ||
            effect.State is not "awaiting-approval" and not "approved" and not "applying" and not "rejected" and not "succeeded" and not "failed" and not "outcome-unknown" ||
            effect.Version < 1)
            throw new ArgumentException("Effect metadata is invalid.", nameof(effect));
    }

    private static void ValidateOperation(ConversationOperation operation)
    {
        DemandId(operation.OperationId, nameof(operation.OperationId));
        DemandId(operation.CommandId, nameof(operation.CommandId));
        if (!Enum.IsDefined(operation.Status) || !Enum.IsDefined(operation.TerminalPolicy) || operation.Attempt < 0 || operation.Version < 0 ||
            operation.SafeReason is { Length: > 256 } || operation.LeaseOwner is { Length: > 256 })
            throw new ArgumentException("Conversation operation metadata is invalid.", nameof(operation));
        if (operation.Approval is { } approval && operation.Effect is { } effect)
        {
            ValidateApproval(approval, operation.OperationId);
            ValidateEffect(effect, operation.OperationId, approval.EffectId);
            ValidateApprovalLifecycle(operation, approval, effect);
        }
        else if (operation.Approval is not null || operation.Effect is not null ||
                 operation.Status == ConversationOperationStatus.AwaitingApproval)
            throw new ArgumentException("Conversation approval metadata is invalid.", nameof(operation));
        if (operation.SuspendedInvocation is not { } invocation) return;
        ValidateInvocationMetadata(invocation);
        if (invocation.InputUtf8 is null || invocation.InputUtf8.Length > 64 * 1024)
            throw new ArgumentException("Conversation operation metadata is invalid.", nameof(operation));
    }

    private static void ValidateApprovalLifecycle(
        ConversationOperation operation,
        ApprovalRecord approval,
        EffectRecord effect)
    {
        var valid = operation.Status switch
        {
            ConversationOperationStatus.AwaitingApproval => approval.State == "requested" && effect.State == "awaiting-approval",
            ConversationOperationStatus.Pending or ConversationOperationStatus.RetryScheduled => approval.State == "approved" && effect.State == "approved",
            ConversationOperationStatus.Running => approval.State == "approved" && effect.State == "applying",
            ConversationOperationStatus.Succeeded => approval.State == "approved" && effect.State == "succeeded",
            ConversationOperationStatus.OutcomeUnknown => approval.State == "approved" && effect.State == "outcome-unknown",
            ConversationOperationStatus.Failed =>
                approval.State == "rejected" && effect.State == "rejected" ||
                approval.State == "approved" && effect.State == "failed",
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Conversation approval/effect state is inconsistent.", nameof(operation));
    }

    private static void ValidateInvocation(SuspendedInvocation invocation, DateTimeOffset now)
    {
        ValidateInvocationMetadata(invocation);
        if (invocation.InputUtf8 is null || invocation.InputUtf8.Length > 64 * 1024 || invocation.AuthorizationExpiresAt <= now)
            throw new ArgumentException("A suspended invocation must be exact, bounded, and unexpired.", nameof(invocation));
    }

    private static void ValidateInvocationMetadata(SuspendedInvocation invocation)
    {
        DemandId(invocation.Provider, nameof(invocation.Provider));
        DemandId(invocation.ToolId, nameof(invocation.ToolId));
        DemandId(invocation.AuthorizationAttemptId, nameof(invocation.AuthorizationAttemptId));
        if (!OAuthCallbackPaths.IsSupportedProvider(invocation.Provider) ||
            !OAuthCallbackPaths.IsOpaqueFlowReference(invocation.AuthorizationFlowReference) ||
            !Guid.TryParseExact(invocation.AuthorizationAttemptId, "N", out _) ||
            invocation.AuthorizationExpiresAt == default ||
            !IsProviderTool(invocation.Provider, invocation.ToolId))
            throw new ArgumentException("A suspended invocation must be exact, bounded, and unexpired.", nameof(invocation));
    }

    private static bool IsProviderTool(string provider, string toolId) =>
        toolId.StartsWith("cross.", StringComparison.Ordinal) ||
        (string.Equals(provider, OAuthCallbackPaths.GoogleProvider, StringComparison.Ordinal)
            ? toolId.StartsWith("gmail.", StringComparison.Ordinal)
            : toolId.StartsWith("salesforce.", StringComparison.Ordinal));

    private static void ValidateOutbox(ConversationOutboxEntry entry)
    {
        DemandId(entry.OutboxId, nameof(entry.OutboxId));
        DemandId(entry.Kind, nameof(entry.Kind));
        if (entry.Sequence < 0 || entry.PayloadUtf8 is null || entry.PayloadUtf8.Length > 64 * 1024)
            throw new ArgumentException("Outbox payloads must be bounded.", nameof(entry));
    }

    private static void DemandId(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Runtime state identifiers must be present and bounded.", name);
    }

    private static void DemandHash(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 digest is required.", name);
    }
}
