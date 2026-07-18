using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel;
using Brain.Kernel.Connections;
using Brain.Modules.Ai;
using Brain.Modules.Flutter;
using Brain.Modules.Google;
using DigitalBrain.Tests;
using Flutter.Contracts;
using Google.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Product;

public sealed class GmailAssistantJourneyTests(
    BrainClusterFixture<ProductJourneyKindsConfigurator> fixture)
    : BrainTest<ProductJourneyKindsConfigurator>(fixture)
{
    private const string Owner = "owner";
    private const string Space = "actor/product";
    private const string ProductSession = "owner|actor/product|session/product";
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    [Fact]
    public async Task Gmail_summary_uses_bounded_AI_renders_UI_and_preserves_the_effect_rail()
    {
        ProductJourneyKindsConfigurator.Reset();
        var gmailAddress = Address(Owner, "gmail/assistant");
        var operationAddress = Address(Owner, "gmail-assistant/main");
        var windowAddress = Address(Owner, "window/main");
        await ConnectGoogleAsync(gmailAddress);

        var requestJson = JsonSerializer.Serialize(new
        {
            maximumMessages = 10,
            reply = new GmailSendProposalRequest(
                "reply@example.com",
                "Re: inbox",
                "Thanks for the update.",
                "journey-reply")
        }, JsonOptions);
        var commandId = CommandId();
        var invocation = new NeuronInvocation(
            GoogleCapabilityIds.GmailInboxSummarize,
            requestJson,
            commandId,
            ProductSession);
        var operation = Cluster.Client.GetGrain<INeuron>(operationAddress);

        var summary = await operation.InvokeAsync(invocation);
        var replay = await operation.InvokeAsync(invocation);

        Assert.Equal(summary, replay);
        Assert.InRange(ProductJourneyKindsConfigurator.GmailProvider.MailboxLimit, 1, 10);
        Assert.InRange(ProductJourneyKindsConfigurator.GmailProvider.MessageReadCalls, 1, 10);
        Assert.Equal(1, ProductJourneyKindsConfigurator.ChatClient.Calls);
        Assert.InRange(
            Encoding.UTF8.GetByteCount(ProductJourneyKindsConfigurator.ChatClient.LastInput),
            1,
            32_768);

        var window = Cluster.Client.GetGrain<INeuron>(windowAddress);
        var windowEvents = await window.ReadEventsAsync(0, 100);
        Assert.Single(windowEvents.Events, entry => entry.Kind == "window.rendered");
        var document = UiDocument.Parse((await window.ReadAsync("default")).StateJson);
        var action = Assert.Single(document.Blocks, block => block.Kind == "button").Action;
        Assert.NotNull(action);
        Assert.Equal(GoogleCapabilityIds.GmailSendPropose, action.Contract);
        Assert.Equal(gmailAddress, action.Target);

        var gmail = Cluster.Client.GetGrain<INeuron>(gmailAddress);
        var proposal = await gmail.InvokeAsync(new(
            action.Contract,
            action.InputJson,
            CommandId(),
            ProductSession));
        Assert.NotNull(proposal.EffectKey);
        Assert.Equal(0, ProductJourneyKindsConfigurator.GmailProvider.SendCalls);

        var effect = Cluster.Client.GetGrain<INeuron>(proposal.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", CommandId(), ProductSession));
        var executeCommand = CommandId();
        var sent = await ExecuteAsync(gmail, proposal.EffectKey!, executeCommand);
        var sentReplay = await ExecuteAsync(gmail, proposal.EffectKey!, executeCommand);

        Assert.Equal(sent, sentReplay);
        Assert.Equal(1, ProductJourneyKindsConfigurator.GmailProvider.SendCalls);
        Assert.Contains("provider-message-id", sent.OutputJson);
        Assert.Contains(
            (await gmail.ReadEventsAsync(0, 100)).Events,
            entry => entry.Kind == "gmail.sent" &&
                     entry.PayloadJson.Contains("provider-message-id", StringComparison.Ordinal));

        ProductJourneyKindsConfigurator.GmailProvider.TimeoutOnSend = true;
        var unknownProposal = await gmail.InvokeAsync(new(
            GoogleCapabilityIds.GmailSendPropose,
            JsonSerializer.Serialize(new GmailSendProposalRequest(
                "reply@example.com",
                "Re: timeout",
                "Do not retry automatically.",
                "journey-timeout"), JsonOptions),
            CommandId(),
            ProductSession));
        var unknownEffect = Cluster.Client.GetGrain<INeuron>(unknownProposal.EffectKey!);
        await unknownEffect.InvokeAsync(new("effect.approve.v1", "{}", CommandId(), ProductSession));
        var unknownCommand = CommandId();
        var unknown = await ExecuteAsync(gmail, unknownProposal.EffectKey!, unknownCommand);
        var unknownReplay = await ExecuteAsync(gmail, unknownProposal.EffectKey!, unknownCommand);

        Assert.Equal(unknown, unknownReplay);
        Assert.Contains("delivery-unknown", unknown.OutputJson);
        Assert.Equal(2, ProductJourneyKindsConfigurator.GmailProvider.SendCalls);

        var foreignPrincipal = Principal("another-owner", Space);
        var policy = new FlutterGatewayPolicy();
        await Assert.ThrowsAsync<BrainException>(() =>
            UiEndpoints.ReadAsync(
                Cluster.Client,
                foreignPrincipal,
                policy,
                Address(Owner, "connection/google-primary"),
                "default"));
        await Assert.ThrowsAsync<BrainException>(() =>
            UiEndpoints.ReadAsync(
                Cluster.Client,
                foreignPrincipal,
                policy,
                windowAddress,
                "default"));

        var operationType = typeof(IGmailNeuron).Assembly.GetType(
            "Google.Contracts.IGmailAssistantOperation");
        Assert.NotNull(operationType);
        var method = Assert.Single(operationType.GetMethods());
        Assert.Equal(
            GoogleCapabilityIds.GmailInboxSummarize,
            Assert.Single(method.GetCustomAttributes(typeof(NeuronContractAttribute), false)
                .Cast<NeuronContractAttribute>()).Contract);
    }

    private async Task ConnectGoogleAsync(string gmailAddress)
    {
        var connection = Cluster.Client.GetGrain<INeuron>(
            Address(Owner, "connection/google-primary"));
        var start = await connection.InvokeAsync(new(
            "connection.start-auth.v1",
            "{}",
            CommandId(),
            ProductSession));
        using var startJson = JsonDocument.Parse(start.OutputJson);
        var authorizationUrl = startJson.RootElement.GetProperty("authorizationUrl").GetString()!;
        var state = new Uri(authorizationUrl).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]) == "state")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .Single();
        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "authorization-code", state }, JsonOptions),
            CommandId(),
            ProductSession));
        await connection.InvokeAsync(new(
            "neuron.grant.v1",
            JsonSerializer.Serialize(new
            {
                granteeKey = gmailAddress,
                contract = "connection.lease-token.v1"
            }, JsonOptions),
            CommandId(),
            ProductSession));
    }

    private static Task<NeuronReceipt> ExecuteAsync(
        INeuron gmail,
        string effectKey,
        string commandId) =>
        gmail.InvokeAsync(new(
            GoogleCapabilityIds.GmailSendExecute,
            JsonSerializer.Serialize(new GmailSendExecutionRequest(effectKey), JsonOptions),
            commandId,
            ProductSession));

    private static ClaimsPrincipal Principal(string owner, string space) =>
        new(new ClaimsIdentity(
            [
                new Claim("digitalbrain:owner", owner),
                new Claim("digitalbrain:space", space)
            ],
            "test"));

    private static string Address(string owner, string neuronId) =>
        new NeuronAddress(owner, Space, neuronId).ToGrainKey();

    private static string CommandId() => Guid.NewGuid().ToString("N");
}

public sealed class ProductJourneyKindsConfigurator : ISiloConfigurator
{
    public static FakeConnectionProvider ConnectionProvider { get; } = new();
    public static JourneyGmailProvider GmailProvider { get; } = new();
    public static JourneyChatClient ChatClient { get; } = new();

    public static void Reset()
    {
        ConnectionProvider.Reset();
        GmailProvider.Reset();
        ChatClient.Reset();
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel();
        siloBuilder.AddDigitalBrainGoogle(
            new ConfigurationBuilder().Build(),
            new TestEnvironment());
        siloBuilder.AddBrainKind("llm", services => new LlmKind(
            new ModelCatalog(
            [
                new ModelBinding(ModelTier.Fast, "journey", "journey-fast"),
                new ModelBinding(ModelTier.Balanced, "journey", "journey-balanced"),
                new ModelBinding(ModelTier.Reasoning, "journey", "journey-reasoning")
            ]),
            services));
        siloBuilder.AddBrainKind("window", _ => new WindowKind());
        siloBuilder.Services.AddKeyedSingleton<IConnectionProvider>("google", ConnectionProvider);
        siloBuilder.Services.AddKeyedSingleton<IGmailProvider>("google", GmailProvider);
        siloBuilder.Services.AddKeyedSingleton<IChatClient>("journey", ChatClient);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "DigitalBrain.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class JourneyGmailProvider : IGmailProvider
{
    private int _messageReadCalls;
    private int _sendCalls;

    public int MailboxLimit { get; private set; }
    public int MessageReadCalls => _messageReadCalls;
    public int SendCalls => _sendCalls;
    public bool TimeoutOnSend { get; set; }

    public void Reset()
    {
        MailboxLimit = 0;
        _messageReadCalls = 0;
        _sendCalls = 0;
        TimeoutOnSend = false;
    }

    public Task<GmailMailboxPage> ReadMailboxAsync(
        ConnectionToken token,
        GmailMailboxReadRequest request,
        CancellationToken ct)
    {
        MailboxLimit = request.Limit;
        var messages = Enumerable.Range(1, Math.Min(12, request.Limit))
            .Select(index => new GmailMessageSummary(
                $"message-{index}",
                $"thread-{index}",
                DateTimeOffset.UnixEpoch.AddMinutes(index),
                $"sender-{index}@example.com",
                $"Subject {index}"))
            .ToArray();
        return Task.FromResult(new GmailMailboxPage(messages));
    }

    public Task<GmailMessage> ReadMessageAsync(
        ConnectionToken token,
        GmailMessageReadRequest request,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _messageReadCalls);
        return Task.FromResult(new GmailMessage(
            request.MessageId,
            "thread",
            DateTimeOffset.UnixEpoch,
            "sender@example.com",
            "Subject",
            new string('x', 8_192)));
    }

    public Task<string> SendAsync(
        ConnectionToken token,
        GmailSendProposal proposal,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _sendCalls);
        if (TimeoutOnSend)
            throw new TimeoutException("ambiguous provider delivery");
        return Task.FromResult("provider-message-id");
    }

    public Task<string> ListAsync(
        ConnectionToken token,
        int max,
        CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<string> SendAsync(
        ConnectionToken token,
        string payloadJson,
        CancellationToken ct) =>
        throw new NotSupportedException();
}

public sealed class JourneyChatClient : IChatClient
{
    private int _calls;

    public int Calls => _calls;
    public string LastInput { get; private set; } = string.Empty;

    public void Reset()
    {
        _calls = 0;
        LastInput = string.Empty;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        LastInput = string.Join("\n", messages.Select(message => message.Text));
        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "Ten messages summarized.")));
    }

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
