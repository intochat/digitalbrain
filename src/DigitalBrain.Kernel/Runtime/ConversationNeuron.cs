using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.runtime.conversation.v1")]
public sealed class ConversationNeuron(
    [PersistentState("conversation", RuntimeStateStorageProviders.Conversations)]
    IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector,
    IGrainFactory grainFactory) : Grain, IConversationNeuron
{
    private EncryptedPersistentState<ConversationState>? _state;

    private EncryptedPersistentState<ConversationState> State => _state ??= new(
        persistentState,
        protector,
        this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Conversation grains require a string key."),
        RuntimeStateKinds.Conversation,
        RuntimeStateSchemas.Conversation,
        ConversationState.Empty,
        static value => value.Revision,
        ConversationTransitions.Validate,
        PrepareArchiveAsync);

    public Task<ConversationState> ReadAsync() => State.ReadAsync();

    public async Task<ConversationArchivePage> ReadArchiveAsync(
        ConversationArchiveCursor? cursor,
        int maximumTurns)
    {
        var state = await State.ReadAsync();
        return await ConversationArchiveTransitions.ReadPageAsync(
            this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Conversation grains require a string key."),
            state.Archive,
            cursor,
            maximumTurns,
            segmentId => grainFactory.GetGrain<IConversationArchiveNeuron>(segmentId).ReadAsync());
    }

    private async Task PrepareArchiveAsync(
        ConversationState current,
        ConversationState next,
        CancellationToken cancellationToken)
    {
        var segment = ConversationArchiveTransitions.PrepareSegment(
            this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Conversation grains require a string key."),
            current,
            next);
        if (segment is null) return;
        var persisted = await grainFactory.GetGrain<IConversationArchiveNeuron>(segment.SegmentId)
            .PutAsync(segment)
            .WaitAsync(cancellationToken);
        if (!ConversationArchiveTransitions.SameSegment(segment, persisted))
            throw new RuntimeStateIntegrityException("conversation archive segment verification failed");
    }

    public Task<ConversationState> InitializeAsync(long expectedRevision, ConversationIdentity identity) =>
        State.UpdateAsync(expectedRevision, current =>
            ConversationTransitions.Initialize(current, expectedRevision, identity));

    public Task<ConversationState> BeginOperationAsync(
        long expectedRevision,
        string commandId,
        string inputHash,
        string operationId,
        string userText,
        DateTimeOffset createdAt) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.BeginOperation(
            current,
            expectedRevision,
            commandId,
            inputHash,
            operationId,
            userText,
            createdAt));

    public Task<ConversationState> AppendTurnAsync(
        long expectedRevision,
        string commandId,
        string inputHash,
        string operationId,
        string role,
        string text,
        DateTimeOffset createdAt) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.AppendTurn(
            current,
            expectedRevision,
            commandId,
            inputHash,
            operationId,
            role,
            text,
            createdAt));

    public Task<ConversationState> PutOperationAsync(long expectedRevision, ConversationOperation operation) =>
        State.UpdateAsync(expectedRevision, current =>
            ConversationTransitions.PutOperation(current, expectedRevision, operation));

    public Task<ConversationState> AppendAssistantTurnAsync(
        long expectedRevision,
        string operationId,
        string text,
        DateTimeOffset createdAt) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.AppendAssistantTurn(
            current,
            expectedRevision,
            operationId,
            text,
            createdAt));

    public Task<ConversationClaim> TryClaimOperationAsync(
        long expectedRevision,
        string operationId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration) =>
        State.UpdateAsync(expectedRevision, current =>
        {
            var result = ConversationTransitions.TryClaimOperation(
                current,
                expectedRevision,
                operationId,
                leaseOwner,
                now,
                leaseDuration);
            return (result.State, result);
        });

    public Task<ConversationClaim> TryClaimAuthorizationAsync(
        long expectedRevision,
        string operationId,
        string authorizationAttemptId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration) =>
        State.UpdateAsync(expectedRevision, current =>
        {
            var result = ConversationTransitions.TryClaimAuthorization(
                current,
                expectedRevision,
                operationId,
                authorizationAttemptId,
                leaseOwner,
                now,
                leaseDuration);
            return (result.State, result);
        });

    public Task<ConversationState> SuspendAuthorizationAsync(
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.SuspendAuthorization(
            current,
            expectedRevision,
            operationId,
            invocation,
            now));

    public Task<ConversationState> SuspendAuthorizationWithAssistantAsync(
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.SuspendAuthorizationWithAssistant(
            current,
            expectedRevision,
            operationId,
            invocation,
            assistantText,
            feedOutbox,
            now));

    public Task<ConversationState> ScheduleRetryAsync(
        long expectedRevision,
        string operationId,
        DateTimeOffset nextAttemptAt,
        string safeReason,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.ScheduleRetry(
            current,
            expectedRevision,
            operationId,
            nextAttemptAt,
            safeReason,
            now));

    public Task<ConversationState> CompleteOperationAsync(
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.CompleteOperation(
            current,
            expectedRevision,
            operationId,
            terminalStatus,
            terminalPolicy,
            safeReason,
            now));

    public Task<ConversationState> CompleteWithAssistantAsync(
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.CompleteWithAssistant(
            current,
            expectedRevision,
            operationId,
            terminalStatus,
            terminalPolicy,
            safeReason,
            assistantText,
            feedOutbox,
            now));

    public Task<ConversationState> EnqueueOutboxAsync(long expectedRevision, ConversationOutboxEntry entry) =>
        State.UpdateAsync(expectedRevision, current =>
            ConversationTransitions.EnqueueOutbox(current, expectedRevision, entry));

    public Task<ConversationState> MarkOutboxDispatchedAsync(
        long expectedRevision,
        string outboxId,
        DateTimeOffset dispatchedAt) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.MarkOutboxDispatched(
            current,
            expectedRevision,
            outboxId,
            dispatchedAt));

    public Task<ConversationState> RecordMigrationAsync(long expectedRevision, string migrationId) =>
        State.UpdateAsync(expectedRevision, current =>
            ConversationTransitions.RecordMigration(current, expectedRevision, migrationId));

    public Task<ConversationState> TombstoneAsync(
        long expectedRevision,
        DateTimeOffset deletedAt,
        string reason) =>
        State.UpdateAsync(expectedRevision, current =>
            ConversationTransitions.Tombstone(current, expectedRevision, deletedAt, reason));
}
