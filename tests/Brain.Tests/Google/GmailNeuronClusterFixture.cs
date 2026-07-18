using Brain.Kernel;
using DigitalBrain.Google;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Google;

[CollectionDefinition(GmailTestCollection.Name, DisableParallelization = true)]
public sealed class GmailTestCollection : ICollectionFixture<GmailNeuronClusterFixture>
{
    public const string Name = "gmail-neurons";
}

public sealed class GmailNeuronClusterFixture : IDisposable
{
    public static FakeGmailMcpClient SharedMcp { get; } = new();

    public TestCluster Cluster { get; }
    public FakeGmailMcpClient Mcp => SharedMcp;

    public GmailNeuronClusterFixture()
    {
        SharedMcp.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public void Dispose()
    {
        Cluster.StopAllSilos();
        Cluster.Dispose();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.UseJsonJournalFormat(GmailJournalJsonContext.Default);
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(ReactiveNeuron<GmailFeedEvent>.DefaultStreamProviderName, configure =>
            {
                configure.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedOnly);
            });
            siloBuilder.Services.AddSingleton<IGmailMcpClient>(_ => SharedMcp);
            siloBuilder.Services.AddSingleton<IChatClient>(_ => new StubChatClient());
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
