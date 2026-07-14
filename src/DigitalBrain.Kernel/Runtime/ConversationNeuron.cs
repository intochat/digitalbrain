using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.runtime.conversation.v1")]
internal sealed class ConversationNeuron(
    [PersistentState("conversation", RuntimeStateStorageProviders.Conversations)]
    IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector,
    IGrainFactory grainFactory) : Grain, IConversationNeuron, IRemindable
{
    private const string OperationReminderName = "ino.operation-worker.v1";
    private static readonly TimeSpan OperationReminderDueTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OperationReminderPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OperationTimerInitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OperationTimerRetryDelay = TimeSpan.FromSeconds(5);
    private EncryptedPersistentState<ConversationState>? _state;
    private IGrainReminder? _operationReminder;
    private IGrainTimer? _operationTimer;
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
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        var state = await State.ReadAsync(cancellationToken);
        if (state.Inbox.Any(entry => state.AcceptedCommands.All(command =>
            !string.Equals(command.CommandId, entry.CommandId, StringComparison.Ordinal))))
        {
            state = await State.UpdateAsync(state.Revision, ConversationTransitions.MigrateLegacyAcceptedCommands, cancellationToken);
        }
        if (state.Outbox.Any(entry => entry.Sequence == 0))
        {
            state = await State.UpdateAsync(state.Revision, ConversationTransitions.MigrateLegacyOutboxSequences, cancellationToken);
        }
        if (state.Operations.Any(operation => operation.SuspendedInvocation is { } invocation && (invocation.InputUtf8.Length > 0 || invocation.Workflow is not null)))
        {
            state = await State.UpdateAsync(state.Revision, ConversationTransitions.RemoveLegacyAuthorizationPayloads, cancellationToken);
        }
        if (HasOperationToWatch(state))
            await EnsureOperationReminderAsync();
        else
            await StopOperationReminderIfIdleAsync(state);
    }
    public async Task<ConversationArchivePage> ReadArchiveAsync(ConversationArchiveCursor? cursor, int maximumTurns)
    {
        var state = await State.ReadAsync();
        return await ConversationArchiveTransitions.ReadPageAsync(
            this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Conversation grains require a string key."),
            state.Archive,
            cursor,
            maximumTurns,
            segmentId => grainFactory.GetGrain<IConversationArchiveNeuron>(segmentId).ReadAsync());
    }
    private async Task PrepareArchiveAsync(ConversationState current, ConversationState next, CancellationToken cancellationToken)
    {
        var segment = ConversationArchiveTransitions.PrepareSegment(this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Conversation grains require a string key."), current, next);
        if (segment is null) return;
        var persisted = await grainFactory.GetGrain<IConversationArchiveNeuron>(segment.SegmentId).PutAsync(segment).WaitAsync(cancellationToken);
        if (!ConversationArchiveTransitions.SameSegment(segment, persisted))
            throw new RuntimeStateIntegrityException("conversation archive segment verification failed");
    }
    public Task<ConversationState> InitializeAsync(long expectedRevision, ConversationIdentity identity) =>
        State.UpdateAsync(expectedRevision, current =>
            ConversationTransitions.Initialize(current, expectedRevision, identity));
    public async Task<ConversationState> BeginOperationAsync(
        long expectedRevision,
        string commandId,
        string inputHash,
        string operationId,
        string userText,
        string requestId,
        ConversationOutboxEntry acceptedOutbox,
        DateTimeOffset createdAt,
        string[]? grants = null)
    {
        var state = await State.UpdateAsync(expectedRevision, current => ConversationTransitions.BeginOperation(current, expectedRevision, commandId, inputHash, operationId, userText, requestId, acceptedOutbox, createdAt, grants));
        await EnsureOperationReminderAsync();
        return state;
    }
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, OperationReminderName, StringComparison.Ordinal)) return;
        await ProcessScheduledOperationsAsync();
    }
    private async Task ReceiveOperationTimerAsync(CancellationToken cancellationToken)
    {
        var timer = _operationTimer;
        _operationTimer = null;
        timer?.Dispose();
        await ProcessScheduledOperationsAsync();
    }
    private async Task ProcessScheduledOperationsAsync()
    {
        var state = await State.ReadAsync();
        var conversationKey = this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Conversation grains require a string key.");
        foreach (var operation in state.Operations.Where(HasOperationToWatch).ToArray())
        {
            var worker = grainFactory.GetGrain<IInoOperationWorkerGrain>(conversationKey + "|" + operation.OperationId);
            await worker.ScheduleAsync();
        }
        var latest = await State.ReadAsync();
        if (HasOperationToWatch(latest))
            EnsureOperationTimer(OperationTimerRetryDelay);
        else
            await StopOperationReminderIfIdleAsync(latest);
    }
    private async Task EnsureOperationReminderAsync()
    {
        _operationReminder ??= await this.RegisterOrUpdateReminder(OperationReminderName, OperationReminderDueTime, OperationReminderPeriod);
        EnsureOperationTimer(OperationTimerInitialDelay);
    }
    private void EnsureOperationTimer(TimeSpan dueTime) =>
        _operationTimer ??= this.RegisterGrainTimer(ReceiveOperationTimerAsync, new GrainTimerCreationOptions(dueTime, Timeout.InfiniteTimeSpan) { KeepAlive = true });
    private async Task StopOperationReminderIfIdleAsync(ConversationState state)
    {
        if (HasOperationToWatch(state)) return;
        _operationTimer?.Dispose();
        _operationTimer = null;
        _operationReminder ??= await this.GetReminder(OperationReminderName);
        if (_operationReminder is null) return;
        await this.UnregisterReminder(_operationReminder);
        _operationReminder = null;
    }
    private static bool HasOperationToWatch(ConversationState state) =>
        state.Outbox.Any(entry => entry.DispatchedAt is null) ||
        state.Operations.Any(operation => operation.Status is
            ConversationOperationStatus.Pending or
            ConversationOperationStatus.AwaitingAuthorization or
            ConversationOperationStatus.RetryScheduled or
            ConversationOperationStatus.Running);
    private static bool HasOperationToWatch(ConversationOperation operation) =>
        operation.Status == ConversationOperationStatus.Pending || operation.Status == ConversationOperationStatus.AwaitingAuthorization ||
        operation.Status == ConversationOperationStatus.RetryScheduled ||
        operation.Status == ConversationOperationStatus.Running;
    public Task<ConversationClaim> TryClaimOperationAsync(
        long expectedRevision,
        string operationId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        ConversationOutboxEntry? runningOutbox = null) =>
        State.UpdateAsync(expectedRevision, current =>
        {
            var result = ConversationTransitions.TryClaimOperation(current, expectedRevision, operationId, leaseOwner, now, leaseDuration, runningOutbox);
            return (result.State, result);
        });
    public Task<ConversationClaim> TryClaimAuthorizationAsync(
        long expectedRevision,
        string operationId,
        string authorizationAttemptId,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        ConversationOutboxEntry? runningOutbox = null) =>
        State.UpdateAsync(expectedRevision, current =>
        {
            var result = ConversationTransitions.TryClaimAuthorization(current, expectedRevision, operationId, authorizationAttemptId, leaseOwner, now, leaseDuration, runningOutbox);
            return (result.State, result);
        });
    public Task<ConversationState> SuspendAuthorizationWithAssistantAsync(
        long expectedRevision,
        string operationId,
        SuspendedInvocation invocation,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        ConversationLeaseFence? leaseFence = null) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.SuspendAuthorizationWithAssistant(current, expectedRevision, operationId, invocation, assistantText, feedOutbox, now, leaseFence));
    public Task<ConversationState> RequestApprovalWithAssistantAsync(
        long expectedRevision,
        string operationId,
        ApprovalRecord approval,
        EffectRecord effect,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        WorkflowReference? workflow = null,
        ConversationLeaseFence? leaseFence = null) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.RequestApprovalWithAssistant(current, expectedRevision, operationId, approval, effect, assistantText, feedOutbox, now, workflow, leaseFence));
    public async Task<ConversationState> DecideApprovalWithAssistantAsync(
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
        var state = await State.UpdateAsync(expectedRevision, current => ConversationTransitions.DecideApprovalWithAssistant(current, expectedRevision, operationId, approvalId, approved, decisionId, decidedBy, assistantText, feedOutbox, now));
        await EnsureOperationReminderAsync();
        return state;
    }
    public async Task<ConversationState> ScheduleRetryAsync(
        long expectedRevision,
        string operationId,
        DateTimeOffset nextAttemptAt,
        string safeReason,
        DateTimeOffset now,
        ConversationOutboxEntry? retryOutbox = null,
        ConversationLeaseFence? leaseFence = null)
    {
        var state = await State.UpdateAsync(expectedRevision, current => ConversationTransitions.ScheduleRetry(current, expectedRevision, operationId, nextAttemptAt, safeReason, now, retryOutbox, leaseFence));
        await EnsureOperationReminderAsync();
        return state;
    }
    public Task<ConversationState> CompleteWithAssistantAsync(
        long expectedRevision,
        string operationId,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        WorkflowReference? workflow = null,
        ConversationLeaseFence? leaseFence = null) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.CompleteWithAssistant(current, expectedRevision, operationId, terminalStatus, terminalPolicy, safeReason, assistantText, feedOutbox, now, workflow, leaseFence));
    public Task<ConversationState> CompleteEffectWithAssistantAsync(
        long expectedRevision,
        string operationId,
        EffectRecord effect,
        ConversationOperationStatus terminalStatus,
        ConversationTerminalPolicy terminalPolicy,
        string? safeReason,
        string assistantText,
        ConversationOutboxEntry feedOutbox,
        DateTimeOffset now,
        ConversationLeaseFence? leaseFence = null) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.CompleteEffectWithAssistant(current, expectedRevision, operationId, effect, terminalStatus, terminalPolicy, safeReason, assistantText, feedOutbox, now, leaseFence));
    public Task<ConversationState> MarkOutboxDispatchedAsync(long expectedRevision, string outboxId, DateTimeOffset dispatchedAt) =>
        State.UpdateAsync(expectedRevision, current => ConversationTransitions.MarkOutboxDispatched(current, expectedRevision, outboxId, dispatchedAt));
    public Task<ConversationState> RecordMigrationAsync(long expectedRevision, string migrationId) =>
        State.UpdateAsync(expectedRevision, current =>
            ConversationTransitions.RecordMigration(current, expectedRevision, migrationId));
}
