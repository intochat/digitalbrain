using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public enum ConversationLifecycle { Uninitialized, Active, Suspended, Completed, Tombstoned }
public enum ConversationOperationStatus { Pending, Running, AwaitingAuthorization, RetryScheduled, Succeeded, Failed, OutcomeUnknown, Cancelled }
public enum ConversationTerminalPolicy { NeverRetry, VerifyBeforeRetry, ManualIntervention }
public enum ConversationTurnKind { User, Assistant, Authorization }

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
    [property: Id(2)] byte[] InputUtf8,
    [property: Id(3)] string AuthorizationAttemptId,
    [property: Id(4)] DateTimeOffset AuthorizationExpiresAt,
    [property: Id(5)] string AuthorizationFlowReference);

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
    [property: Id(10)] DateTimeOffset UpdatedAt);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-outbox-entry")]
public sealed record ConversationOutboxEntry(
    [property: Id(0)] string OutboxId,
    [property: Id(1)] string Kind,
    [property: Id(2)] byte[] PayloadUtf8,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] DateTimeOffset? DispatchedAt);

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
    [property: Id(2)] bool Claimed);

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
        DateTimeOffset createdAt);
    [Alias("digitalbrain.runtime.conversation.append-turn")]
    Task<ConversationState> AppendTurnAsync(
        long expectedRevision,
        string commandId,
        string inputHash,
        string operationId,
        string role,
        string text,
        DateTimeOffset createdAt);
    [Alias("digitalbrain.runtime.conversation.put-operation")]
    Task<ConversationState> PutOperationAsync(long expectedRevision, ConversationOperation operation);
    [Alias("digitalbrain.runtime.conversation.append-assistant-turn")]
    Task<ConversationState> AppendAssistantTurnAsync(
        long expectedRevision,
        string operationId,
        string text,
        DateTimeOffset createdAt);
    [Alias("digitalbrain.runtime.conversation.try-claim-operation")]
    Task<ConversationClaim> TryClaimOperationAsync(
        long expectedRevision,
        string operationId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration);
    [Alias("digitalbrain.runtime.conversation.try-claim-authorization")]
    Task<ConversationClaim> TryClaimAuthorizationAsync(
        long expectedRevision,
        string operationId,
        string authorizationAttemptId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration);
    [Alias("digitalbrain.runtime.conversation.suspend-authorization")]
    Task<ConversationState> SuspendAuthorizationAsync(
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        DateTimeOffset now);
    [Alias("digitalbrain.runtime.conversation.suspend-authorization-with-assistant")]
    Task<ConversationState> SuspendAuthorizationWithAssistantAsync(
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now);
    [Alias("digitalbrain.runtime.conversation.schedule-retry")]
    Task<ConversationState> ScheduleRetryAsync(
        long expectedRevision,
        string operationId,
        DateTimeOffset nextAttemptAt,
        string safeReason,
        DateTimeOffset now);
    [Alias("digitalbrain.runtime.conversation.complete-operation")]
    Task<ConversationState> CompleteOperationAsync(
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        DateTimeOffset now);
    [Alias("digitalbrain.runtime.conversation.complete-with-assistant")]
    Task<ConversationState> CompleteWithAssistantAsync(
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now);
    [Alias("digitalbrain.runtime.conversation.enqueue-outbox")]
    Task<ConversationState> EnqueueOutboxAsync(long expectedRevision, ConversationOutboxEntry entry);
    [Alias("digitalbrain.runtime.conversation.mark-outbox-dispatched")]
    Task<ConversationState> MarkOutboxDispatchedAsync(long expectedRevision, string outboxId, DateTimeOffset dispatchedAt);
    [Alias("digitalbrain.runtime.conversation.record-migration")]
    Task<ConversationState> RecordMigrationAsync(long expectedRevision, string migrationId);
    [Alias("digitalbrain.runtime.conversation.tombstone")]
    Task<ConversationState> TombstoneAsync(long expectedRevision, DateTimeOffset deletedAt, string reason);
}

public static class ConversationTransitions
{
    public const int MaximumInlineTurns = 128;
    public const int MaximumInboxEntries = 256;
    public const int MaximumTerminalOperations = 128;
    public const int MaximumDispatchedOutboxEntries = 128;
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
        DateTimeOffset createdAt)
    {
        DemandMutable(state, expectedRevision);
        DemandId(commandId, nameof(commandId));
        DemandId(operationId, nameof(operationId));
        DemandHash(inputHash, nameof(inputHash));
        ValidateTurnText(userText, nameof(userText));
        var prior = state.Inbox.FirstOrDefault(entry => string.Equals(entry.CommandId, commandId, StringComparison.Ordinal));
        if (prior is not null)
        {
            if (!string.Equals(prior.InputHash, inputHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(prior.OperationId, operationId, StringComparison.Ordinal))
                throw new InvalidOperationException("A command id cannot be reused with different input or operation identity.");
            return state;
        }
        var existingOperation = state.Operations.FirstOrDefault(operation =>
            string.Equals(operation.OperationId, operationId, StringComparison.Ordinal));
        if (existingOperation is not null)
            throw new InvalidOperationException("An operation id cannot be rebound to another command.");
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
            createdAt);
        var next = AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.User,
            commandId,
            userText,
            createdAt) with
        {
            Revision = checked(state.Revision + 1),
            Inbox = state.Inbox.Append(new(commandId, inputHash.ToLowerInvariant(), operationId, createdAt)).ToArray(),
            Operations = state.Operations.Append(operation).ToArray()
        };
        return ValidateAndCompact(next);
    }

    public static ConversationState AppendTurn(
        ConversationState state,
        long expectedRevision,
        string commandId,
        string inputHash,
        string operationId,
        string role,
        string text,
        DateTimeOffset createdAt)
    {
        DemandMutable(state, expectedRevision);
        DemandId(commandId, nameof(commandId));
        DemandId(operationId, nameof(operationId));
        DemandHash(inputHash, nameof(inputHash));
        if (role is not "user")
            throw new ArgumentException("Command inbox turns must use the user role.", nameof(role));
        ValidateTurnText(text, nameof(text));
        var prior = state.Inbox.FirstOrDefault(entry => string.Equals(entry.CommandId, commandId, StringComparison.Ordinal));
        if (prior is not null)
        {
            if (!string.Equals(prior.InputHash, inputHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(prior.OperationId, operationId, StringComparison.Ordinal))
                throw new InvalidOperationException("A command id cannot be reused with different input or operation identity.");
            return state;
        }
        var next = AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.User,
            commandId,
            text,
            createdAt) with
        {
            Revision = checked(state.Revision + 1),
            Inbox = state.Inbox.Append(new(commandId, inputHash.ToLowerInvariant(), operationId, createdAt)).ToArray()
        };
        return ValidateAndCompact(next);
    }

    public static ConversationState PutOperation(ConversationState state, long expectedRevision, ConversationOperation operation)
    {
        DemandMutable(state, expectedRevision);
        ValidateOperation(operation);
        var existing = state.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal));
        if (existing == operation) return state;
        if (existing is not null && !string.Equals(existing.CommandId, operation.CommandId, StringComparison.Ordinal))
            throw new InvalidOperationException("An operation cannot be rebound to another command.");
        var operations = state.Operations.Where(candidate =>
            !string.Equals(candidate.OperationId, operation.OperationId, StringComparison.Ordinal)).Append(operation).ToArray();
        return ValidateAndCompact(state with { Revision = checked(state.Revision + 1), Operations = operations });
    }

    public static ConversationState AppendAssistantTurn(
        ConversationState state,
        long expectedRevision,
        string operationId,
        string text,
        DateTimeOffset createdAt)
    {
        DemandMutable(state, expectedRevision);
        DemandId(operationId, nameof(operationId));
        ValidateTurnText(text, nameof(text));
        var operation = RequiredOperation(state, operationId);
        if (!IsTerminal(operation.Status))
            throw new InvalidOperationException("An assistant result requires a terminal operation.");
        DemandUserTurn(state, operation);
        var existing = FindTurn(state, operationId, ConversationTurnKind.Assistant, operationId);
        if (existing is not null)
        {
            if (string.Equals(existing.Text, text, StringComparison.Ordinal)) return state;
            throw new InvalidOperationException("A terminal operation cannot publish a different assistant result.");
        }
        var next = AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Assistant,
            operationId,
            text,
            createdAt) with
        {
            Revision = checked(state.Revision + 1)
        };
        return ValidateAndCompact(next);
    }

    public static ConversationClaim TryClaimOperation(
        ConversationState state,
        long expectedRevision,
        string operationId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        DemandMutable(state, expectedRevision);
        DemandId(operationId, nameof(operationId));
        DemandId(leaseOwner, nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var operation = RequiredOperation(state, operationId);
        if (IsTerminal(operation.Status) || operation.Status == ConversationOperationStatus.AwaitingAuthorization ||
            operation.NextAttemptAt is { } due && due > now ||
            operation.LeaseExpiresAt is { } leaseExpiry && leaseExpiry > now)
            return new(state, operation, false);
        var claimed = operation with
        {
            Status = ConversationOperationStatus.Running,
            Attempt = checked(operation.Attempt + 1),
            NextAttemptAt = null,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = now.Add(leaseDuration),
            UpdatedAt = now
        };
        var next = ReplaceOperation(state, claimed);
        return new(next, claimed, true);
    }

    public static ConversationClaim TryClaimAuthorization(
        ConversationState state,
        long expectedRevision,
        string operationId,
        string authorizationAttemptId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        DemandMutable(state, expectedRevision);
        DemandId(operationId, nameof(operationId));
        DemandId(authorizationAttemptId, nameof(authorizationAttemptId));
        DemandId(leaseOwner, nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var operation = RequiredOperation(state, operationId);
        var invocation = operation.SuspendedInvocation;
        if (operation.Status != ConversationOperationStatus.AwaitingAuthorization || invocation is null ||
            !string.Equals(invocation.AuthorizationAttemptId, authorizationAttemptId, StringComparison.Ordinal) ||
            invocation.AuthorizationExpiresAt <= now ||
            operation.LeaseExpiresAt is { } leaseExpiry && leaseExpiry > now)
            return new(state, operation, false);
        var claimed = operation with
        {
            Status = ConversationOperationStatus.Running,
            Attempt = checked(operation.Attempt + 1),
            NextAttemptAt = null,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = now.Add(leaseDuration),
            UpdatedAt = now
        };
        var next = ReplaceOperation(state with { Lifecycle = ConversationLifecycle.Active }, claimed);
        return new(next, claimed, true);
    }

    public static ConversationState SuspendAuthorization(
        ConversationState state,
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        ValidateInvocation(invocation, now);
        var operation = RequiredOperation(state, operationId);
        if (IsTerminal(operation.Status)) throw new InvalidOperationException("A terminal operation cannot be suspended.");
        return ReplaceOperation(state with { Lifecycle = ConversationLifecycle.Suspended }, operation with
        {
            Status = ConversationOperationStatus.AwaitingAuthorization,
            SuspendedInvocation = invocation with { InputUtf8 = invocation.InputUtf8.ToArray() },
            LeaseOwner = null,
            LeaseExpiresAt = null,
            NextAttemptAt = invocation.AuthorizationExpiresAt,
            UpdatedAt = now
        });
    }

    public static ConversationState SuspendAuthorizationWithAssistant(
        ConversationState state,
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now)
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
        var suspended = operation with
        {
            Status = ConversationOperationStatus.AwaitingAuthorization,
            SuspendedInvocation = invocation with { InputUtf8 = invocation.InputUtf8.ToArray() },
            LeaseOwner = null,
            LeaseExpiresAt = null,
            NextAttemptAt = invocation.AuthorizationExpiresAt,
            UpdatedAt = now
        };
        var next = AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Authorization,
            invocation.AuthorizationAttemptId,
            assistantText,
            now) with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Suspended,
            Operations = ReplaceOperationWithoutRevision(state.Operations, suspended),
            Outbox = state.Outbox.Append(CopyOutbox(feedOutbox)).ToArray()
        };
        return ValidateAndCompact(next);
    }

    public static ConversationState ScheduleRetry(
        ConversationState state,
        long expectedRevision,
        string operationId,
        DateTimeOffset nextAttemptAt,
        string safeReason,
        DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        if (nextAttemptAt <= now || string.IsNullOrWhiteSpace(safeReason) || safeReason.Length > 256)
            throw new ArgumentException("Retry scheduling requires a future due time and bounded safe reason.");
        var operation = RequiredOperation(state, operationId);
        if (IsTerminal(operation.Status)) throw new InvalidOperationException("A terminal operation cannot be retried.");
        return ReplaceOperation(state with { Lifecycle = ConversationLifecycle.Active }, operation with
        {
            Status = ConversationOperationStatus.RetryScheduled,
            NextAttemptAt = nextAttemptAt,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SuspendedInvocation = null,
            SafeReason = safeReason,
            UpdatedAt = now
        });
    }

    public static ConversationState CompleteOperation(
        ConversationState state,
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        if (!IsTerminal(terminalStatus)) throw new ArgumentException("The requested operation state is not terminal.", nameof(terminalStatus));
        if (safeReason is { Length: > 256 }) throw new ArgumentException("Safe reasons must be bounded.", nameof(safeReason));
        var operation = RequiredOperation(state, operationId);
        if (IsTerminal(operation.Status))
        {
            if (operation.Status == terminalStatus && operation.TerminalPolicy == terminalPolicy &&
                string.Equals(operation.SafeReason, safeReason, StringComparison.Ordinal)) return state;
            throw new InvalidOperationException("A terminal operation cannot change its outcome.");
        }
        return ReplaceOperation(state with { Lifecycle = ConversationLifecycle.Active }, operation with
        {
            Status = terminalStatus,
            TerminalPolicy = terminalPolicy,
            SafeReason = safeReason,
            NextAttemptAt = null,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            SuspendedInvocation = null,
            UpdatedAt = now
        });
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
        DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        ValidateTerminal(terminalStatus, safeReason);
        ValidateTurnText(assistantText, nameof(assistantText));
        ValidatePendingOutbox(feedOutbox);
        var operation = RequiredOperation(state, operationId);
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
            UpdatedAt = now
        };
        var next = AppendTurnRecord(
            state,
            operationId,
            ConversationTurnKind.Assistant,
            operationId,
            assistantText,
            now) with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Active,
            Operations = ReplaceOperationWithoutRevision(state.Operations, terminal),
            Outbox = state.Outbox.Append(CopyOutbox(feedOutbox)).ToArray()
        };
        return ValidateAndCompact(next);
    }

    public static ConversationState EnqueueOutbox(ConversationState state, long expectedRevision, ConversationOutboxEntry entry)
    {
        DemandMutable(state, expectedRevision);
        ValidateOutbox(entry);
        var existing = state.Outbox.FirstOrDefault(candidate => string.Equals(candidate.OutboxId, entry.OutboxId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (existing.Kind == entry.Kind && existing.PayloadUtf8.AsSpan().SequenceEqual(entry.PayloadUtf8)) return state;
            throw new InvalidOperationException("An outbox id cannot be reused for different content.");
        }
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            Outbox = state.Outbox.Append(entry with { PayloadUtf8 = entry.PayloadUtf8.ToArray() }).ToArray()
        });
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

    public static ConversationState Tombstone(ConversationState state, long expectedRevision, DateTimeOffset deletedAt, string reason)
    {
        DemandRevision(state, expectedRevision);
        if (state.Lifecycle == ConversationLifecycle.Tombstoned) return state;
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 256) throw new ArgumentException("A bounded tombstone reason is required.", nameof(reason));
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            Lifecycle = ConversationLifecycle.Tombstoned,
            Tombstone = new(deletedAt, reason),
            Operations = [],
            Outbox = [],
            Inbox = []
        });
    }

    public static void Validate(ConversationState state)
    {
        if (state.SchemaVersion != RuntimeStateSchemas.Conversation || state.Revision < 0 ||
            !Enum.IsDefined(state.Lifecycle) || state.Turns is null || state.Inbox is null ||
            state.Operations is null || state.Outbox is null || state.AppliedMigrationIds is null)
            throw new RuntimeStateIntegrityException("invalid conversation schema");
        if (state.Revision == 0 && state.Identity is not null || state.Revision > 0 && state.Identity is null)
            throw new RuntimeStateIntegrityException("invalid conversation identity lifecycle");
        if (state.Identity is not null) ValidateIdentity(state.Identity);
        if (state.Turns.Length > MaximumInlineTurns || state.Inbox.Length > MaximumInboxEntries ||
            state.AppliedMigrationIds.Length > MaximumMigrationIds ||
            state.Operations.Count(operation => IsTerminal(operation.Status)) > MaximumTerminalOperations ||
            state.Outbox.Count(entry => entry.DispatchedAt is not null) > MaximumDispatchedOutboxEntries)
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
        foreach (var operation in state.Operations) ValidateOperation(operation);
        foreach (var entry in state.Outbox) ValidateOutbox(entry);
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
        first.AuthorizationExpiresAt == second.AuthorizationExpiresAt && first.InputUtf8.AsSpan().SequenceEqual(second.InputUtf8);

    private static bool SameOutbox(ConversationOutboxEntry first, ConversationOutboxEntry second) =>
        string.Equals(first.OutboxId, second.OutboxId, StringComparison.Ordinal) &&
        string.Equals(first.Kind, second.Kind, StringComparison.Ordinal) && first.CreatedAt == second.CreatedAt &&
        first.PayloadUtf8.AsSpan().SequenceEqual(second.PayloadUtf8);

    private static ConversationOutboxEntry CopyOutbox(ConversationOutboxEntry entry) =>
        entry with { PayloadUtf8 = entry.PayloadUtf8.ToArray() };

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
        var terminal = state.Operations.Where(operation => IsTerminal(operation.Status))
            .OrderBy(operation => operation.UpdatedAt).TakeLast(MaximumTerminalOperations);
        var pendingOutbox = state.Outbox.Where(entry => entry.DispatchedAt is null);
        var dispatchedOutbox = state.Outbox.Where(entry => entry.DispatchedAt is not null)
            .OrderBy(entry => entry.DispatchedAt).TakeLast(MaximumDispatchedOutboxEntries);
        return state with
        {
            Inbox = state.Inbox.OrderBy(entry => entry.RecordedAt).TakeLast(MaximumInboxEntries).ToArray(),
            Operations = active.Concat(terminal).OrderBy(operation => operation.UpdatedAt).ToArray(),
            Outbox = pendingOutbox.Concat(dispatchedOutbox).OrderBy(entry => entry.CreatedAt).ToArray(),
            AppliedMigrationIds = state.AppliedMigrationIds.TakeLast(MaximumMigrationIds).ToArray()
        };
    }

    private static ConversationOperation RequiredOperation(ConversationState state, string operationId) =>
        state.Operations.FirstOrDefault(operation => string.Equals(operation.OperationId, operationId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException("Conversation operation not found.");

    private static bool IsTerminal(ConversationOperationStatus status) => status is
        ConversationOperationStatus.Succeeded or ConversationOperationStatus.Failed or
        ConversationOperationStatus.OutcomeUnknown or ConversationOperationStatus.Cancelled;

    private static void DemandMutable(ConversationState state, long expectedRevision)
    {
        DemandRevision(state, expectedRevision);
        if (state.Identity is null || state.Lifecycle is ConversationLifecycle.Uninitialized or ConversationLifecycle.Tombstoned)
            throw new InvalidOperationException("Conversation state is not mutable.");
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

    private static void ValidateOperation(ConversationOperation operation)
    {
        DemandId(operation.OperationId, nameof(operation.OperationId));
        DemandId(operation.CommandId, nameof(operation.CommandId));
        if (!Enum.IsDefined(operation.Status) || !Enum.IsDefined(operation.TerminalPolicy) || operation.Attempt < 0 ||
            operation.SafeReason is { Length: > 256 } || operation.LeaseOwner is { Length: > 256 })
            throw new ArgumentException("Conversation operation metadata is invalid.", nameof(operation));
        if (operation.SuspendedInvocation is not { } invocation) return;
        ValidateInvocationMetadata(invocation);
        if (invocation.InputUtf8 is null || invocation.InputUtf8.Length > 64 * 1024)
            throw new ArgumentException("Conversation operation metadata is invalid.", nameof(operation));
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
        if (entry.PayloadUtf8 is null || entry.PayloadUtf8.Length > 64 * 1024)
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
