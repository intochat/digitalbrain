using System.Reflection;
using Brain.Contracts;
using Brain.Kernel;
using Brain.Tests.Kernel;
using DigitalBrain.Google;
using Microsoft.Extensions.AI;
using Xunit;

namespace Brain.Tests.Google;

public sealed class GmailNeuronTests
{
    private static readonly NeuronAddress Self = new(
        new OrganizationId("org-1"),
        new SpaceId("space-1"),
        "google.gmail.v1",
        "gmail-1");

    private static SynapseMetadata Meta(Guid commandId) =>
        new(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: new OrganizationId("org-1"),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: new SpaceId("space-1"),
            Source: Self,
            SourceSequence: 1,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

    private static (GmailReactiveCore Core, FakeGmailMcpClient Mcp, OrderingReactiveStore Store) CreateCore()
    {
        var store = new OrderingReactiveStore();
        var mcp = new FakeGmailMcpClient();
        mcp.OnSend = () => store.Order.Add("provider");
        mcp.OnList = () => store.Order.Add("provider");
        var core = new GmailReactiveCore(store, mcp, Self);
        return (core, mcp, store);
    }

    [Fact]
    public void Gmail_contract_exposes_only_typed_operations()
    {
        var methods = typeof(IGmail).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(methods, m => m.Name == nameof(IGmail.ListMessagesAsync));
        Assert.Contains(methods, m => m.Name == nameof(IGmail.SendMessageAsync));
        Assert.Contains(methods, m => m.Name == nameof(IGmail.GetSurfaceAsync));
        Assert.Contains(methods, m => m.Name == nameof(IGmail.GetIdentityAsync));

        Assert.Equal(
            typeof(CommandSynapse<GmailListRequest>),
            typeof(IGmail).GetMethod(nameof(IGmail.ListMessagesAsync))!.GetParameters().Single().ParameterType);
        Assert.Equal(
            typeof(CommandSynapse<GmailSendRequest>),
            typeof(IGmail).GetMethod(nameof(IGmail.SendMessageAsync))!.GetParameters().Single().ParameterType);
        Assert.Equal(
            typeof(Task<UiSurfaceSnapshot>),
            typeof(IGmail).GetMethod(nameof(IGmail.GetSurfaceAsync))!.ReturnType);

        Assert.DoesNotContain(
            methods,
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)
                && method.Name is not nameof(IGmail.GetIdentityAsync)));
        Assert.DoesNotContain(methods, m => m.Name.Contains("Invoke", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, m => m.Name.Contains("Dispatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gmail_agent_uses_typed_MCP_tools()
    {
        var mcp = new FakeGmailMcpClient();
        var chat = new StubChatClient();
        var tools = GmailMcpTools.CreateTypedTools(mcp);
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, tool => tool.Name == GmailMcpTools.ListToolName);
        Assert.Contains(tools, tool => tool.Name == GmailMcpTools.SendToolName);
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("invoke", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("generic", StringComparison.OrdinalIgnoreCase));

        var agent = GmailMcpTools.CreateAgent(chat, mcp);
        Assert.Equal("gmail-agent", agent.Name);
        Assert.NotNull(agent.ChatClient);
    }

    [Fact]
    public async Task Read_result_updates_UiSurface_through_outbox_and_feed_event()
    {
        var (core, mcp, _) = CreateCore();
        mcp.ListResult = new GmailMessageListResult(3, "three");
        var commandId = Guid.NewGuid();

        var receipt = await core.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(commandId), new GmailListRequest("is:inbox", 10)));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(1, mcp.ListCalls);
        Assert.Equal(1, core.Outbox.Count);
        Assert.Equal(GmailFeedEvent.UiSurfaceKind, core.Outbox[0].Event.Payload.Kind);

        await core.DrainOutboxAsync();
        Assert.Equal(0, core.Outbox.Count);

        var surface = core.GetSurface();
        Assert.Equal(GmailReactiveCore.SurfaceId, surface.Surface.SurfaceId);
        Assert.Equal("messages:3", surface.Surface.Blocks[0].Text);
        Assert.True(surface.Surface.Revision >= 1);
    }

    [Fact]
    public async Task Mutation_intent_is_durable_before_provider_call()
    {
        var (core, mcp, store) = CreateCore();
        var commandId = Guid.NewGuid();

        var receipt = await core.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId),
                new GmailSendRequest("a@example.com", "Subject", "SECRET_BODY")));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(0, mcp.SendCalls);
        Assert.Contains("commit", store.Order);
        Assert.DoesNotContain("provider", store.Order);
        Assert.Equal(1, core.Outbox.Count);
        Assert.Equal(GmailFeedEvent.SendEffectKind, core.Outbox[0].Event.Payload.Kind);
        Assert.Equal(commandId.ToString("N"), core.Outbox[0].Event.Payload.IdempotencyKey);
        Assert.Equal("send-pending", core.GetSurface().Surface.Blocks[0].Text);

        await core.DrainOutboxAsync();

        Assert.Equal(1, mcp.SendCalls);
        Assert.Equal(["commit", "provider", "commit"], store.Order.ToArray());
        Assert.Equal("send-completed", core.GetSurface().Surface.Blocks[0].Text);
        Assert.True(core.Flags.ContainsKey(GmailReactiveCore.EffectDoneFlagPrefix + commandId.ToString("N")));
    }

    [Fact]
    public async Task Duplicate_effect_does_not_repeat_provider_mutation()
    {
        var (core, mcp, _) = CreateCore();
        var commandId = Guid.NewGuid();
        await core.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId),
                new GmailSendRequest("a@example.com", "Subject", "body")));

        var intent = core.Outbox[0];
        await core.ExecuteOutboxIntentAsync(intent);
        Assert.Equal(1, mcp.SendCalls);

        await core.ExecuteOutboxIntentAsync(intent);
        Assert.Equal(1, mcp.SendCalls);

        await core.DrainOutboxAsync();
        Assert.Equal(1, mcp.SendCalls);
    }

    [Fact]
    public async Task Provider_failure_is_not_swallowed()
    {
        var (core, mcp, _) = CreateCore();
        mcp.SendException = new InvalidOperationException("provider down with token=abc body=secret");
        var commandId = Guid.NewGuid();
        await core.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId),
                new GmailSendRequest("a@example.com", "Subject", "body")));

        var ex = await Assert.ThrowsAsync<BrainException>(() => core.DrainOutboxAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);
        Assert.DoesNotContain("token=abc", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
        Assert.NotEmpty(core.Failures);
        Assert.Equal(BrainErrors.FailureSanitized, core.Failures[^1].Code);
        Assert.Equal(ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage, core.Failures[^1].Message);
    }

    [Fact]
    public async Task Provider_credentials_and_message_bodies_are_absent_from_telemetry()
    {
        var (core, mcp, _) = CreateCore();
        mcp.ListResult = new GmailMessageListResult(1, "one");
        var listId = Guid.NewGuid();
        var sendId = Guid.NewGuid();
        const string body = "CONFIDENTIAL_MESSAGE_BODY";
        const string credential = "oauth-token-XYZ";

        await core.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(listId), new GmailListRequest("from:boss", 5)));
        await core.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(sendId),
                new GmailSendRequest("a@example.com", "Hello", body)));
        await core.DrainOutboxAsync();

        var blob = string.Join('\n', core.Telemetry);
        Assert.DoesNotContain(body, blob, StringComparison.Ordinal);
        Assert.DoesNotContain(credential, blob, StringComparison.Ordinal);
        Assert.DoesNotContain("oauth", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONFIDENTIAL", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("a@example.com", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", blob, StringComparison.Ordinal);
        Assert.DoesNotContain("from:boss", blob, StringComparison.Ordinal);
    }

    private sealed class OrderingReactiveStore : IReactiveStore<GmailFeedEvent>
    {
        private readonly InMemoryReactiveStore<GmailFeedEvent> _inner = new();
        public List<string> Order { get; } = [];
        public bool FailNextCommit { get => _inner.FailNextCommit; set => _inner.FailNextCommit = value; }
        public IDictionary<string, CommandReceipt> Receipts => _inner.Receipts;
        public IDictionary<string, byte> ProcessedEvents => _inner.ProcessedEvents;
        public IDictionary<string, long> SourceSequences => _inner.SourceSequences;
        public IList<OutboxIntent<GmailFeedEvent>> Outbox => _inner.Outbox;
        public IDictionary<string, string> Domain => _inner.Domain;
        public IDictionary<string, string> Flags => _inner.Flags;
        public IList<SanitizedFailure> Failures => _inner.Failures;
        public IDictionary<string, byte> AcceptedCausation => _inner.AcceptedCausation;
        public IDictionary<string, byte> RejectedCausation => _inner.RejectedCausation;

        public Task CommitAsync()
        {
            Order.Add("commit");
            return _inner.CommitAsync();
        }
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
