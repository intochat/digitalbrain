extern alias McpProject;

using System.Text;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;
using RuntimeSurfaceFeed = McpProject::DigitalBrain.Mcp.RuntimeSurfaceFeed;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

// RuntimeSurfaceFeed is constructed directly (outside the Orleans cluster) by MCP request handlers, so it
// is tested here against hand-rolled grain fakes backed by the real Conversation/SurfaceFeed transition
// functions, matching the pattern already used for RuntimeSessionAuthority/ConversationRecoveryWorker in
// this project rather than standing up a full Orleans TestCluster for a single MCP-layer class.
public sealed class RuntimeSurfaceFeedTests
{
    [Fact]
    public async Task PrepareSessionAsync_rematerializes_a_poisoned_surface_from_the_corrected_conversation_on_the_first_delivery()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);

        // Simulates a surface projected before this fix, from a conversation turn that carried a raw
        // (non-internal) Salesforce authorization URL -- the exact poisoned payload the Flutter decoder
        // rejects (runtime_controller_test.dart's legacy-Salesforce-URL regression scenario).
        var poisoned = new InoConversationSnapshot(
            conversationId,
            1,
            [new InoConversationTurn("command-1", "assistant", "Connect Salesforce to continue.", InoConversationStates.Succeeded)],
            [new InoConversationOperation(
                "command-1",
                "connect salesforce",
                InoConversationStates.Succeeded,
                null,
                false,
                now,
                new ToolAction("openUrl", "Connect Salesforce", "https://login.salesforce.com/services/oauth2/authorize?raw=legacy"),
                null,
                null,
                null)]);

        var conversationNeuron = new FakeConversationNeuron(ConversationState.Empty());
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var cluster = new FakeClusterClient(conversationNeuron, surfaceFeedNeuron);
        var feed = new RuntimeSurfaceFeed(cluster, TimeProvider.System);

        // Seed the durable surface with the poisoned projection, as if it were written before recovery
        // corrected the conversation.
        await feed.ProjectConversationAsync(context, poisoned, "poisoned-projection", now, CancellationToken.None);

        // Recovery neutralizes the conversation out-of-band (an append-only corrected revision) -- the
        // already-persisted surface is not touched by that correction.
        conversationNeuron.Current = BuildConversationState(
            context, conversationId, revision: 4, assistantText: "Salesforce is already connected.", now);

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);

        var home = prepared.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var payloadText = Encoding.UTF8.GetString(home.PayloadUtf8);
        Assert.Contains("Salesforce is already connected.", payloadText, StringComparison.Ordinal);
        Assert.DoesNotContain("raw=legacy", payloadText, StringComparison.Ordinal);
        Assert.DoesNotContain("login.salesforce.com", payloadText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareSessionAsync_rematerializes_a_legacy_surface_when_the_conversation_revision_is_unchanged()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);
        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var cluster = new FakeClusterClient(conversationNeuron, surfaceFeedNeuron);
        var feed = new RuntimeSurfaceFeed(cluster, TimeProvider.System);

        await feed.ProjectConversationAsync(
            context,
            InoConversationSnapshot.Empty(context),
            "current-projection",
            now,
            CancellationToken.None);

        using var legacyPayloadDocument = JsonDocument.Parse("""
            {"data":{"action":{"target":"https://login.salesforce.com/services/oauth2/authorize?raw=legacy"}}}
            """);
        var legacyPresentation = new
        {
            context.CorrelationId,
            CauseKind = "conversation",
            CauseId = conversationId,
            RequiredClientCapabilities = ConversationSurfacePayload.RequiredCapabilities,
            Payload = legacyPayloadDocument.RootElement.Clone(),
            ConversationRevision = 1
        };
        var home = surfaceFeedNeuron.Current.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        surfaceFeedNeuron.Current = surfaceFeedNeuron.Current with
        {
            CurrentSurfaces = surfaceFeedNeuron.Current.CurrentSurfaces.Select(surface =>
                ReferenceEquals(surface, home)
                    ? surface with { PayloadUtf8 = JsonSerializer.SerializeToUtf8Bytes(legacyPresentation) }
                    : surface).ToArray()
        };
        conversationNeuron.Current = BuildConversationState(
            context, conversationId, revision: 1, assistantText: "Salesforce is already connected.", now);

        var prepared = await feed.PrepareSessionAsync(context, CancellationToken.None);

        var rematerialized = prepared.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var payloadText = Encoding.UTF8.GetString(rematerialized.PayloadUtf8);
        Assert.Contains("Salesforce is already connected.", payloadText, StringComparison.Ordinal);
        Assert.DoesNotContain("raw=legacy", payloadText, StringComparison.Ordinal);
        Assert.DoesNotContain("login.salesforce.com", payloadText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareSessionAsync_does_not_reproject_content_when_the_conversation_revision_is_unchanged()
    {
        var now = new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
        var context = Context();
        var conversationId = InoConversationIdentity.From(context);

        var conversationNeuron = new FakeConversationNeuron(
            BuildConversationState(context, conversationId, revision: 1, assistantText: "Hello.", now));
        var surfaceFeedNeuron = new FakeSurfaceFeedNeuron(SurfaceFeedState.Empty());
        var cluster = new FakeClusterClient(conversationNeuron, surfaceFeedNeuron);
        var feed = new RuntimeSurfaceFeed(cluster, TimeProvider.System);

        var first = await feed.PrepareSessionAsync(context, CancellationToken.None);
        var second = await feed.PrepareSessionAsync(context, CancellationToken.None);

        var firstHome = first.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var secondHome = second.State.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        Assert.Equal(
            Encoding.UTF8.GetString(firstHome.PayloadUtf8),
            Encoding.UTF8.GetString(secondHome.PayloadUtf8));
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

    private static ConversationState BuildConversationState(
        RuntimeRequestContext context, string conversationId, long revision, string assistantText, DateTimeOffset now) => new(
        RuntimeStateSchemas.Conversation,
        revision,
        ConversationLifecycle.Active,
        new ConversationIdentity(context.TenantId, context.WorkspaceId, context.Principal, conversationId),
        [new ConversationTurn(1, "assistant", assistantText, now, "operation-1", ConversationTurnKind.Assistant, "operation-1")],
        [],
        [new ConversationOperation(
            "operation-1", "command-1", ConversationOperationStatus.Succeeded, 1, null, null, null,
            ConversationTerminalPolicy.NeverRetry, null, null, now)],
        [],
        null,
        null,
        []);

    private sealed class FakeConversationNeuron(ConversationState initial) : IConversationNeuron
    {
        public ConversationState Current { get; set; } = initial;

        public Task<ConversationState> ReadAsync() => Task.FromResult(Current);

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
        public Task<ConversationClaim> TryClaimOperationAsync(
            long expectedRevision, string operationId, string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration) =>
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

    private sealed class FakeSurfaceFeedNeuron(SurfaceFeedState initial) : ISurfaceFeedNeuron
    {
        public SurfaceFeedState Current { get; set; } = initial;

        public Task<SurfaceFeedState> ReadAsync() => Task.FromResult(Current);

        public Task<SurfaceFeedState> InitializeAsync(long expectedRevision, SurfaceFeedIdentity identity)
        {
            Current = SurfaceFeedTransitions.Initialize(Current, expectedRevision, identity);
            return Task.FromResult(Current);
        }

        public Task<SurfaceFeedState> ApplyProjectionAsync(long expectedRevision, SurfaceFeedProjection projection, DateTimeOffset now)
        {
            Current = SurfaceFeedTransitions.ApplyProjection(Current, expectedRevision, projection, now);
            return Task.FromResult(Current);
        }

        public Task<SurfaceFeedState> RecordDeliveryAsync(long expectedRevision, string deliveryId, long sequence, DateTimeOffset deliveredAt) =>
            throw new NotSupportedException();
        public Task<SurfaceFeedState> AcknowledgeAsync(
            long expectedRevision, string sessionScopeHash, long sequence, DateTimeOffset cursorExpiresAt, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<SurfaceFeedState> RevokeSessionAsync(long expectedRevision, string sessionScopeHash, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<SurfaceActionConsumption> ConsumeActionAsync(
            long expectedRevision, string bindingId, string tokenHash, string idempotencyKey, string operationId, DateTimeOffset now) =>
            throw new NotSupportedException();
        public Task<SurfaceFeedState> RebuildAsync(long expectedRevision, string projectionId, DateTimeOffset now) =>
            throw new NotSupportedException();
    }

    // Routes by requested grain interface type rather than key -- this test only ever seeds one conversation
    // and one surface feed, so a full key-addressed grain directory would add nothing but ceremony.
    private sealed class FakeClusterClient(FakeConversationNeuron conversation, FakeSurfaceFeedNeuron surfaceFeed) : IClusterClient
    {
        public IServiceProvider ServiceProvider => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey
        {
            if (typeof(TGrainInterface) == typeof(IConversationNeuron)) return (TGrainInterface)(object)conversation;
            if (typeof(TGrainInterface) == typeof(ISurfaceFeedNeuron)) return (TGrainInterface)(object)surfaceFeed;
            throw new NotSupportedException($"Unexpected grain interface {typeof(TGrainInterface)}.");
        }

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
