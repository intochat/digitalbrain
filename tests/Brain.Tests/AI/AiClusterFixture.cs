using System.Text.Json.Serialization;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.AI;

[CollectionDefinition(AiTestCollection.Name, DisableParallelization = true)]
public sealed class AiTestCollection : ICollectionFixture<AiClusterFixture>
{
    public const string Name = "ai-neurons";
}

public sealed class AiClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }
    public ScriptedChatClient GptClient { get; } = new("gpt-reply-1", "gpt-reply-2", "gpt-reply-3");
    public ScriptedChatClient GrokClient { get; } = new("grok-reply-1", "grok-reply-2", "grok-reply-3");

    public AiClusterFixture()
    {
        SharedAiTestClients.Gpt = GptClient;
        SharedAiTestClients.Grok = GrokClient;

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
            siloBuilder.UseJsonJournalFormat(AiTestJournalJsonContext.Default);
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(ReactiveNeuron<GroupChatStepEvent>.DefaultStreamProviderName, configure =>
            {
                configure.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedOnly);
            });
            siloBuilder.Services.AddOptions<AiProviderOptions>();
            siloBuilder.Services.Configure<AiProviderOptions>(options =>
            {
                options.ProviderTimeout = TimeSpan.FromMilliseconds(250);
                options.MaximumDiscussionSteps = 8;
            });
            siloBuilder.Services.AddKeyedSingleton<Microsoft.Extensions.AI.IChatClient>(
                AiServiceKeys.Gpt56ChatClient,
                (_, _) => SharedAiTestClients.Gpt ?? new ScriptedChatClient("gpt-reply"));
            siloBuilder.Services.AddKeyedSingleton<Microsoft.Extensions.AI.IChatClient>(
                AiServiceKeys.Grok45ChatClient,
                (_, _) => SharedAiTestClients.Grok ?? new ScriptedChatClient("grok-reply"));
        }
    }
}

internal static class SharedAiTestClients
{
    public static ScriptedChatClient? Gpt { get; set; }
    public static ScriptedChatClient? Grok { get; set; }
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(CommandReceipt))]
[JsonSerializable(typeof(CommandReceiptStatus))]
[JsonSerializable(typeof(SanitizedFailure))]
[JsonSerializable(typeof(OrganizationId))]
[JsonSerializable(typeof(PrincipalId))]
[JsonSerializable(typeof(SpaceId))]
[JsonSerializable(typeof(NeuronAddress))]
[JsonSerializable(typeof(SynapseMetadata))]
[JsonSerializable(typeof(EventSynapse<string>))]
[JsonSerializable(typeof(OutboxIntent<string>))]
[JsonSerializable(typeof(GroupChatStepEvent))]
[JsonSerializable(typeof(EventSynapse<GroupChatStepEvent>))]
[JsonSerializable(typeof(OutboxIntent<GroupChatStepEvent>))]
[JsonSerializable(typeof(AgentTurnRequest))]
[JsonSerializable(typeof(AgentTurnResult))]
internal sealed partial class AiTestJournalJsonContext : JsonSerializerContext;
