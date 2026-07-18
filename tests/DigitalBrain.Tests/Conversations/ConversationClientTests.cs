using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Conversations;

public sealed class TestConversationNeuron : IConversationGrain;

public sealed class ConversationClientTests
{
    [Fact]
    public async Task Open_derives_the_owner_scoped_canonical_grain_identity()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var owner = new BrainOwnerId("owner-a");
        var brain = new DigitalBrainClient(cluster.Client, owner);
        var conversationId = new ConversationId("main");

        var conversation = brain.Conversations.Open(conversationId);

        Assert.Equal(
            ConversationKey.Encode(owner, conversationId),
            conversation.GetPrimaryKeyString());
    }

    [Fact]
    public async Task Owner_submits_and_reads_turns_in_an_owned_conversation()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-a");

        try
        {
            var brain = new DigitalBrainClient(cluster.Client, new BrainOwnerId("owner-a"));
            var conversation = brain.Conversations.Open(new ConversationId("main"));
            var turnId = ConversationTurnId.New();

            var result = await conversation.SubmitTurnAsync(
                new ConversationTurnRequest(turnId, ConversationRole.Fast, "hello"));
            var snapshot = await conversation.ReadAsync();

            Assert.Equal(turnId, result.TurnId);
            Assert.Equal(ConversationRole.Fast, result.Role);
            Assert.Equal("openai:gpt-5-mini:hello", result.Response);
            var committed = Assert.Single(snapshot.Turns);
            Assert.Equal(turnId, committed.TurnId);
            Assert.Equal("hello", committed.Text);
            Assert.Equal(result.Response, committed.Response);
            Assert.Equal(result.Revision, snapshot.Revision);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Repeated_turn_identity_is_committed_once_through_the_real_provider_adapter()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-idempotent");
        ConversationProviderTransport.Reset("idempotent");

        try
        {
            var brain = new DigitalBrainClient(cluster.Client, new BrainOwnerId("owner-idempotent"));
            var conversation = brain.Conversations.Open(new ConversationId("main"));
            var request = new ConversationTurnRequest(
                ConversationTurnId.New(),
                ConversationRole.Balanced,
                "idempotent");

            var first = await conversation.SubmitTurnAsync(request);
            var second = await conversation.SubmitTurnAsync(request);
            var snapshot = await conversation.ReadAsync();

            Assert.Equal(first, second);
            Assert.Equal("anthropic:claude-sonnet-4-5:idempotent", first.Response);
            Assert.Single(snapshot.Turns);
            Assert.Equal(1, snapshot.Revision);
            Assert.Equal(1, ConversationProviderTransport.Count("idempotent"));
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Missing_stream_delivery_cannot_hide_the_durable_final_result()
    {
        await using var cluster = await ConversationCluster.CreateAsync(includeStreams: false);
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-stream-repair");
        ConversationProviderTransport.Reset("stream-repair");

        try
        {
            var brain = new DigitalBrainClient(cluster.Client, new BrainOwnerId("owner-stream-repair"));
            var conversation = brain.Conversations.Open(new ConversationId("main"));
            var request = new ConversationTurnRequest(
                ConversationTurnId.New(),
                ConversationRole.Reasoning,
                "stream-repair");

            var deliveryFailure = await Assert.ThrowsAsync<BrainException>(() =>
                conversation.SubmitTurnAsync(request));
            var repaired = await conversation.SubmitTurnAsync(request);
            var snapshot = await conversation.ReadAsync();

            Assert.Equal(NeuronFailureKind.ProviderUnavailable, deliveryFailure.FailureKind);
            Assert.Equal("openai:gpt-5:stream-repair", repaired.Response);
            Assert.Equal(repaired.Response, Assert.Single(snapshot.Turns).Response);
            Assert.Equal(1, ConversationProviderTransport.Count("stream-repair"));
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Foreign_owner_conversation_key_is_denied_server_side()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-a");

        try
        {
            var foreignKey = ConversationKey.Encode(new BrainOwnerId("owner-b"), new ConversationId("main"));
            var forged = cluster.Client.GetGrain<IConversationNeuron>(foreignKey);

            var denied = await Assert.ThrowsAsync<BrainException>(() => forged.ReadAsync());

            Assert.Equal(NeuronFailureKind.AuthorizationDenied, denied.FailureKind);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Malformed_conversation_keys_are_denied_server_side()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-a");

        try
        {
            var malformed = cluster.Client.GetGrain<IConversationNeuron>("owner-a");

            var denied = await Assert.ThrowsAsync<BrainException>(() => malformed.ReadAsync());

            Assert.Equal(NeuronFailureKind.AuthorizationDenied, denied.FailureKind);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Unauthenticated_conversation_calls_are_denied_server_side()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var key = ConversationKey.Encode(new BrainOwnerId("owner-a"), new ConversationId("main"));

        var denied = await Assert.ThrowsAsync<BrainException>(
            () => cluster.Client.GetGrain<IConversationNeuron>(key).ReadAsync());

        Assert.Equal(NeuronFailureKind.AuthenticationRequired, denied.FailureKind);
    }

    [Fact]
    public void Conversation_notification_stream_identity_is_the_complete_canonical_key_per_owner()
    {
        var conversation = new ConversationId("main");
        var ownerAKey = ConversationKey.Encode(new BrainOwnerId("owner-a"), conversation);
        var ownerBKey = ConversationKey.Encode(new BrainOwnerId("owner-b"), conversation);

        var ownerAStream = StreamId.Create(NeuronNotificationPublisher.StreamNamespace, ownerAKey);
        var ownerBStream = StreamId.Create(NeuronNotificationPublisher.StreamNamespace, ownerBKey);

        Assert.Equal(ownerAKey, ownerAStream.GetKeyAsString());
        Assert.NotEqual(ownerAStream, ownerBStream);
    }

    [Fact]
    public async Task Foreign_owner_cannot_subscribe_to_conversation_notifications()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        ownerContext.Current = new BrainOwnerId("owner-a");

        try
        {
            var foreignKey = ConversationKey.Encode(
                new BrainOwnerId("owner-b"),
                new ConversationId("main"));
            var stream = cluster.Client
                .GetStreamProvider(NeuronNotificationPublisher.StreamProviderName)
                .GetStream<NeuronNotification>(
                    StreamId.Create(NeuronNotificationPublisher.StreamNamespace, foreignKey));

            var denied = await Assert.ThrowsAsync<BrainException>(() =>
                stream.SubscribeAsync((_, _) => Task.CompletedTask));

            Assert.Equal(NeuronFailureKind.AuthorizationDenied, denied.FailureKind);
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }

    [Fact]
    public async Task Owner_can_subscribe_to_owned_conversation_notifications()
    {
        await using var cluster = await ConversationCluster.CreateAsync();
        var ownerContext = cluster.ServiceProvider.GetRequiredService<BrainOwnerContext>();
        var owner = new BrainOwnerId("owner-a");
        ownerContext.Current = owner;

        try
        {
            var key = ConversationKey.Encode(owner, new ConversationId("owned"));
            var stream = cluster.Client
                .GetStreamProvider(NeuronNotificationPublisher.StreamProviderName)
                .GetStream<NeuronNotification>(
                    StreamId.Create(NeuronNotificationPublisher.StreamNamespace, key));

            var handle = await stream.SubscribeAsync((_, _) => Task.CompletedTask);
            await handle.UnsubscribeAsync();
        }
        finally
        {
            ownerContext.Current = null;
            RequestContext.Remove(nameof(BrainOwnerId));
        }
    }
}

file static class ConversationCluster
{
    public static async Task<TestCluster> CreateAsync(bool includeStreams = true)
    {
        var builder = new TestClusterBuilder();
        ConversationSiloConfigurator.IncludeStreams = includeStreams;
        ConversationClientConfigurator.IncludeStreams = includeStreams;
        builder.AddSiloBuilderConfigurator<ConversationSiloConfigurator>();
        builder.AddClientBuilderConfigurator<ConversationClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class ConversationSiloConfigurator : ISiloConfigurator
    {
        public static bool IncludeStreams { get; set; } = true;

        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddBrainKernel();
            siloBuilder.AddDigitalBrainAI(
                DigitalBrainAIRegistrationTests.CompleteConfiguration(),
                ConversationProviderTransport.CreateClients());
            if (IncludeStreams)
                siloBuilder.AddMemoryStreams(NeuronNotificationPublisher.StreamProviderName, _ => { });
        }
    }

    private sealed class ConversationClientConfigurator : IClientBuilderConfigurator
    {
        public static bool IncludeStreams { get; set; } = true;

        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            if (IncludeStreams)
                clientBuilder.AddMemoryStreams(NeuronNotificationPublisher.StreamProviderName, _ => { });
            clientBuilder.AddDigitalBrainClient();
        }
    }
}

file static class ConversationProviderTransport
{
    private static readonly ConcurrentDictionary<string, int> CallsByInput =
        new(StringComparer.Ordinal);

    public static DigitalBrainAIHttpClients CreateClients() =>
        new(
            new HttpClient(new ProviderTestHttpHandler(OpenAIResponse)),
            new HttpClient(new ProviderTestHttpHandler(AnthropicResponse))
            {
                Timeout = Timeout.InfiniteTimeSpan
            });

    public static void Reset(string input) => CallsByInput[input] = 0;

    public static int Count(string input) => CallsByInput.GetValueOrDefault(input);

    private static async Task<HttpResponseMessage> OpenAIResponse(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);
        var model = json.RootElement.GetProperty("model").GetString()!;
        var input = json.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content")
            .GetString()!;
        CallsByInput.AddOrUpdate(input, 1, static (_, count) => count + 1);
        return ProviderTestHttpHandler.Json(
            OpenAIProviderClientTests.ChatResponseJson(model, $"openai:{model}:{input}"));
    }

    private static async Task<HttpResponseMessage> AnthropicResponse(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);
        var model = json.RootElement.GetProperty("model").GetString()!;
        var input = json.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;
        CallsByInput.AddOrUpdate(input, 1, static (_, count) => count + 1);
        return ProviderTestHttpHandler.Json(
            AnthropicProviderClientTests.MessageResponseJson(
                $"anthropic:{model}:{input}"));
    }
}
