extern alias McpProject;

using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using ConversationOperationLeaseUnavailableException = McpProject::DigitalBrain.Mcp.ConversationOperationLeaseUnavailableException;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

// Reproduces two MCP replicas (or a recovery sweep racing a live request) claiming the same conversation
// operation concurrently via a genuinely interleaved Task.WhenAll race, against a grain fake that
// serializes calls the way a real Orleans single-activation grain would -- not a sequential
// throws-then-succeeds counter stub.
public sealed class ConversationStateClientConcurrencyTests
{
    [Fact]
    public async Task TransitionAsync_under_concurrent_claims_exactly_one_caller_wins_and_the_other_gets_a_clean_lease_conflict()
    {
        var now = new DateTimeOffset(2026, 7, 12, 11, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var identity = new ConversationIdentity(context.TenantId, context.WorkspaceId, context.Principal, conversationId);

        var seed = ConversationTransitions.Initialize(ConversationState.Empty(), 0, identity);
        seed = ConversationTransitions.BeginOperation(
            seed, seed.Revision, "command-1", new string('a', 64), "operation-1", "connect salesforce", now);

        var neuron = new FakeConversationNeuron(seed);
        var cluster = new FakeClusterClient(neuron);
        var clientA = new ConversationStateClient(cluster, null!, TimeProvider.System);
        var clientB = new ConversationStateClient(cluster, null!, TimeProvider.System);
        var scopedContext = context with { ConversationId = conversationId };

        var taskA = RaceAsync(clientA, scopedContext);
        var taskB = RaceAsync(clientB, scopedContext);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, result => !result.Failed);
        Assert.Single(results, result => result.Failed);
        var winner = results.Single(result => !result.Failed).Snapshot!;
        Assert.Equal(InoConversationStates.Running, winner.CurrentOperation!.State);
    }

    private static async Task<(InoConversationSnapshot? Snapshot, bool Failed)> RaceAsync(
        ConversationStateClient client, RuntimeRequestContext context)
    {
        try
        {
            return (await client.TransitionAsync(context, "command-1", InoConversationStates.Running, CancellationToken.None), false);
        }
        catch (ConversationOperationLeaseUnavailableException)
        {
            return (null, true);
        }
    }

    private static RuntimeRequestContext Context() => new(
        new TenantId("tenant"),
        new WorkspaceId("workspace"),
        new PrincipalRef("principal", PrincipalKind.User),
        "session",
        AuthAssurance.Oidc,
        "correlation",
        null,
        new HashSet<string>(["ui.action"], StringComparer.Ordinal));

    // Serializes TryClaimOperationAsync behind a semaphore -- the same turn-based mutual exclusion a real
    // Orleans single-activation grain gives for free -- so two callers that both read revision N race to
    // commit, and whichever loses genuinely observes a stale expectedRevision at commit time.
    private sealed class FakeConversationNeuron(ConversationState initial) : IConversationNeuron
    {
        private readonly SemaphoreSlim _turn = new(1, 1);

        // Both racing callers must have read the same pre-claim revision before either is allowed to
        // proceed to TryClaimOperationAsync -- deterministically forces the interleaving that a real race
        // between two MCP replicas relies on scheduler luck for, instead of a Task.Yield() gamble.
        private readonly TaskCompletionSource _bothReaders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readers;

        public ConversationState Current { get; private set; } = initial;

        public async Task<ConversationState> ReadAsync()
        {
            if (Interlocked.Increment(ref _readers) >= 2) _bothReaders.TrySetResult();
            await _bothReaders.Task.ConfigureAwait(false);
            return Current;
        }

        public async Task<ConversationClaim> TryClaimOperationAsync(
            long expectedRevision, string operationId, string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration)
        {
            await _turn.WaitAsync().ConfigureAwait(false);
            try
            {
                var result = ConversationTransitions.TryClaimOperation(
                    Current, expectedRevision, operationId, leaseOwner, now, leaseDuration);
                Current = result.State;
                return result;
            }
            finally
            {
                _turn.Release();
            }
        }

        public Task<ConversationArchivePage> ReadArchiveAsync(ConversationArchiveCursor? cursor, int maximumTurns) =>
            throw new NotSupportedException();
        public Task<ConversationState> InitializeAsync(long expectedRevision, ConversationIdentity identity) =>
            throw new NotSupportedException();
        public Task<ConversationState> BeginOperationAsync(
            long expectedRevision, string commandId, string inputHash, string operationId, string userText, DateTimeOffset createdAt) =>
            throw new NotSupportedException();
        public Task<ConversationState> AppendTurnAsync(
            long expectedRevision, string commandId, string inputHash, string operationId, string role, string text, DateTimeOffset createdAt) =>
            throw new NotSupportedException();
        public Task<ConversationState> PutOperationAsync(long expectedRevision, ConversationOperation operation) =>
            throw new NotSupportedException();
        public Task<ConversationState> AppendAssistantTurnAsync(
            long expectedRevision, string operationId, string text, DateTimeOffset createdAt) =>
            throw new NotSupportedException();
        public Task<ConversationClaim> TryClaimAuthorizationAsync(
            long expectedRevision, string operationId, string authorizationAttemptId, string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration) =>
            throw new NotSupportedException();
        public Task<ConversationState> SuspendAuthorizationAsync(
            long expectedRevision, string operationId, SuspendedInvocation invocation, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<ConversationState> SuspendAuthorizationWithAssistantAsync(
            long expectedRevision, string operationId, SuspendedInvocation invocation, string assistantText,
            ConversationOutboxEntry feedOutbox, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<ConversationState> ScheduleRetryAsync(
            long expectedRevision, string operationId, DateTimeOffset nextAttemptAt, string safeReason, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<ConversationState> CompleteOperationAsync(
            long expectedRevision, string operationId, ConversationOperationStatus terminalStatus,
            ConversationTerminalPolicy terminalPolicy, string? safeReason, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<ConversationState> CompleteWithAssistantAsync(
            long expectedRevision, string operationId, ConversationOperationStatus terminalStatus,
            ConversationTerminalPolicy terminalPolicy, string? safeReason, string assistantText,
            ConversationOutboxEntry feedOutbox, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<ConversationState> EnqueueOutboxAsync(long expectedRevision, ConversationOutboxEntry entry) =>
            throw new NotSupportedException();
        public Task<ConversationState> MarkOutboxDispatchedAsync(long expectedRevision, string outboxId, DateTimeOffset dispatchedAt) =>
            throw new NotSupportedException();
        public Task<ConversationState> RecordMigrationAsync(long expectedRevision, string migrationId) =>
            throw new NotSupportedException();
        public Task<ConversationState> TombstoneAsync(long expectedRevision, DateTimeOffset deletedAt, string reason) =>
            throw new NotSupportedException();
    }

    private sealed class FakeClusterClient(FakeConversationNeuron conversation) : IClusterClient
    {
        public IServiceProvider ServiceProvider => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey =>
            typeof(TGrainInterface) == typeof(IConversationNeuron)
                ? (TGrainInterface)(object)conversation
                : throw new NotSupportedException($"Unexpected grain interface {typeof(TGrainInterface)}.");

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string? grainClassNamePrefix = null) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => throw new NotSupportedException();

        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
    }
}
