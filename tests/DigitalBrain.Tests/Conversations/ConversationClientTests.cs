using System.Collections.Concurrent;
using DigitalBrain;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Conversations;

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
            var committed = Assert.Single(snapshot.Turns);
            Assert.Equal(turnId, committed.TurnId);
            Assert.Equal("hello", committed.Text);
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

[Alias(nameof(ITestConversationNeuron))]
public interface ITestConversationNeuron : IConversationNeuron;

public sealed class TestConversationNeuron([NeuronState] NeuronDurableState state)
    : Neuron(state), ITestConversationNeuron, IConversationGrain
{
    private static readonly ConcurrentDictionary<string, List<ConversationTurn>> TurnsByKey = new();

    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnRequest request)
    {
        var turns = TurnsByKey.GetOrAdd(this.GetPrimaryKeyString(), static _ => []);
        turns.Add(new ConversationTurn(request.TurnId, request.Role, request.Text, $"echo:{request.Text}"));
        return Task.FromResult(new ConversationTurnResult(
            request.TurnId,
            request.Role,
            $"echo:{request.Text}",
            turns.Count));
    }

    public Task<ConversationSnapshot> ReadAsync()
    {
        var turns = TurnsByKey.GetOrAdd(this.GetPrimaryKeyString(), static _ => []);
        return Task.FromResult(new ConversationSnapshot(turns.ToArray(), turns.Count));
    }
}

file static class ConversationCluster
{
    public static async Task<TestCluster> CreateAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<ConversationSiloConfigurator>();
        builder.AddClientBuilderConfigurator<ConversationClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class ConversationSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(NeuronNotificationPublisher.StreamProviderName, _ => { });
            siloBuilder.AddBrainKernel();
        }
    }

    private sealed class ConversationClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            clientBuilder
                .AddMemoryStreams(NeuronNotificationPublisher.StreamProviderName, _ => { })
                .AddDigitalBrainClient();
    }
}
