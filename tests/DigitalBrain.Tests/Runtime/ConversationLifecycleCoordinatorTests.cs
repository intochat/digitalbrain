extern alias McpProject;

using System.Text;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using ConversationLifecycleCoordinator = McpProject::DigitalBrain.Mcp.ConversationLifecycleCoordinator;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using IActiveConversationFeed = McpProject::DigitalBrain.Mcp.IActiveConversationFeed;
using IConversationLifecycleState = McpProject::DigitalBrain.Mcp.IConversationLifecycleState;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class ConversationLifecycleCoordinatorTests
{
    [Fact]
    public async Task Create_derives_a_distinct_id_and_preserves_existing_conversations()
    {
        var context = Context();
        var originalId = InoConversationIdentity.From(context);
        var original = Snapshot(originalId, "original turn");
        var unrelated = Snapshot(ConversationId('c'), "unrelated turn");
        var world = new LifecycleWorld(originalId, original, unrelated);
        var coordinator = new ConversationLifecycleCoordinator(world, world, TimeProvider.System);

        var result = await coordinator.CreateAsync(context, originalId, "lifecycle-create-1");

        Assert.True(result.Activated);
        Assert.NotEqual(originalId, result.ConversationId);
        Assert.Equal(result.ConversationId, world.ActiveConversationId);
        Assert.Equal(original, world.Conversations[originalId]);
        Assert.Equal(unrelated, world.Conversations[unrelated.ConversationId]);
    }

    [Fact]
    public async Task Delete_tombstones_only_the_expected_conversation_and_activates_a_replacement()
    {
        var context = Context();
        var deletedId = InoConversationIdentity.From(context);
        var deleted = Snapshot(deletedId, "delete me");
        var unrelated = Snapshot(ConversationId('d'), "keep me");
        var world = new LifecycleWorld(deletedId, deleted, unrelated);
        var sessionBefore = world.SessionSentinel;
        var credentialsBefore = world.ProviderCredentialSentinel;
        var coordinator = new ConversationLifecycleCoordinator(world, world, TimeProvider.System);

        var result = await coordinator.DeleteAsync(context, deletedId, "lifecycle-delete-1");

        Assert.True(result.Activated);
        Assert.Equal(deletedId, result.DeletedConversationId);
        Assert.Contains(deletedId, world.TombstonedIds);
        Assert.DoesNotContain(unrelated.ConversationId, world.TombstonedIds);
        Assert.Equal(unrelated, world.Conversations[unrelated.ConversationId]);
        Assert.NotEqual(deletedId, result.ConversationId);
        Assert.Equal(result.ConversationId, world.ActiveConversationId);
        Assert.Same(sessionBefore, world.SessionSentinel);
        Assert.Same(credentialsBefore, world.ProviderCredentialSentinel);
    }

    [Fact]
    public async Task Stale_delete_never_replaces_the_newer_active_conversation()
    {
        var context = Context();
        var staleId = InoConversationIdentity.From(context);
        var newer = Snapshot(ConversationId('e'), "new active turn");
        var stale = Snapshot(staleId, "old turn");
        var world = new LifecycleWorld(newer.ConversationId, stale, newer);
        var coordinator = new ConversationLifecycleCoordinator(world, world, TimeProvider.System);

        var result = await coordinator.DeleteAsync(context, staleId, "stale-delete-1");

        Assert.False(result.Activated);
        Assert.Contains(staleId, world.TombstonedIds);
        Assert.Equal(newer.ConversationId, world.ActiveConversationId);
        Assert.Equal(newer, world.Conversations[newer.ConversationId]);
    }

    [Fact]
    public void Tombstone_clears_pending_work_authorization_inbox_and_outbox_but_retains_turn_history()
    {
        var now = new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
        var state = ConversationTransitions.Initialize(
            ConversationState.Empty(),
            0,
            new ConversationIdentity(
                new TenantId("tenant"),
                new WorkspaceId("workspace"),
                new PrincipalRef("principal", PrincipalKind.User),
                ConversationId('a')));
        state = ConversationTransitions.BeginOperation(
            state,
            state.Revision,
            "command-1",
            new string('1', 64),
            "operation-1",
            "pending prompt",
            now);
        var claim = ConversationTransitions.TryClaimOperation(
            state,
            state.Revision,
            "operation-1",
            "worker",
            now,
            TimeSpan.FromMinutes(1));
        state = ConversationTransitions.SuspendAuthorizationWithAssistant(
            claim.State,
            claim.State.Revision,
            "operation-1",
            new SuspendedInvocation(
                "salesforce",
                "salesforce.query",
                Encoding.UTF8.GetBytes("{}"),
                "0123456789abcdef0123456789abcdef",
                now.AddMinutes(10),
                "abcdefghijklmnopqrstuvwxyzABCDEF"),
            "Authorization is required.",
            new ConversationOutboxEntry("outbox-1", "surface-feed", [1], now, null),
            now);
        var turns = state.Turns;
        var identity = state.Identity;

        var tombstoned = ConversationTransitions.Tombstone(
            state,
            state.Revision,
            now.AddMinutes(1),
            "user-requested-conversation-delete");

        Assert.Equal(ConversationLifecycle.Tombstoned, tombstoned.Lifecycle);
        Assert.Equal(identity, tombstoned.Identity);
        Assert.Equal(turns, tombstoned.Turns);
        Assert.Empty(tombstoned.Operations);
        Assert.Empty(tombstoned.Inbox);
        Assert.Empty(tombstoned.Outbox);
        Assert.NotNull(tombstoned.Tombstone);
    }

    [Fact]
    public async Task State_client_resolves_missing_conversation_to_the_active_feed()
    {
        var context = Context();
        var activeId = ConversationId('b');
        var world = new LifecycleWorld(activeId, Snapshot(activeId, "active"));
        var client = new ConversationStateClient(null!, world, TimeProvider.System);

        var resolved = await client.ResolveContextAsync(context, CancellationToken.None);
        var explicitContext = context with { ConversationId = InoConversationIdentity.From(context) };
        var explicitResolved = await client.ResolveContextAsync(explicitContext, CancellationToken.None);

        Assert.Equal(activeId, resolved.ConversationId);
        Assert.Equal(explicitContext.ConversationId, explicitResolved.ConversationId);
        Assert.Equal(1, world.ResolveCalls);
        Assert.NotEqual(
            ConversationStateClient.OperationId(resolved, "same-command"),
            ConversationStateClient.OperationId(explicitResolved, "same-command"));
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

    private static InoConversationSnapshot Snapshot(string conversationId, string text) => new(
        conversationId,
        1,
        [new InoConversationTurn("command", "user", text, InoConversationStates.Succeeded)],
        []);

    private static string ConversationId(char character) => "ino-" + new string(character, 64);

    private sealed class LifecycleWorld : IConversationLifecycleState, IActiveConversationFeed
    {
        public LifecycleWorld(string activeConversationId, params InoConversationSnapshot[] conversations)
        {
            ActiveConversationId = activeConversationId;
            Conversations = conversations.ToDictionary(item => item.ConversationId, StringComparer.Ordinal);
        }

        public Dictionary<string, InoConversationSnapshot> Conversations { get; }
        public HashSet<string> TombstonedIds { get; } = new(StringComparer.Ordinal);
        public string ActiveConversationId { get; private set; }
        public object SessionSentinel { get; } = new();
        public object ProviderCredentialSentinel { get; } = new();
        public int ResolveCalls { get; private set; }

        public Task<InoConversationSnapshot> EnsureConversationAsync(
            RuntimeRequestContext context,
            string conversationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Conversations.TryGetValue(conversationId, out var conversation))
            {
                conversation = new InoConversationSnapshot(conversationId, 0, [], []);
                Conversations.Add(conversationId, conversation);
            }
            return Task.FromResult(conversation);
        }

        public Task TombstoneConversationAsync(
            RuntimeRequestContext context,
            string conversationId,
            string reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TombstonedIds.Add(conversationId);
            return Task.CompletedTask;
        }

        public Task<string> ResolveActiveConversationIdAsync(
            RuntimeRequestContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCalls++;
            return Task.FromResult(ActiveConversationId);
        }

        public Task<bool> TryActivateConversationAsync(
            RuntimeRequestContext context,
            string expectedConversationId,
            InoConversationSnapshot conversation,
            string projectionId,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(ActiveConversationId, conversation.ConversationId, StringComparison.Ordinal))
                return Task.FromResult(true);
            if (!string.Equals(ActiveConversationId, expectedConversationId, StringComparison.Ordinal))
                return Task.FromResult(false);
            ActiveConversationId = conversation.ConversationId;
            return Task.FromResult(true);
        }
    }
}
