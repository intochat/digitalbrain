using DigitalBrain.Core;
using DigitalBrain.Ino;
using DigitalBrain.Ino.Context;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Db;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Kernel.Ui;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.TestKit;

// No-op scoped factory for in-process test clusters: always defers to the global IChatClient by returning null.
// Prevents shared-config tests from acquiring a hidden Ollama/OpenAI network dependency.
// Tests that need the recording factory override this via their own ISiloConfigurator.
internal sealed class NoOpScopedChatClientFactory : IScopedChatClientFactory
{
    public IChatClient? Create(string provider, string? apiKey) => null;
}

// Shared in-process cluster kernel wiring: in-memory dual journals plus current INO/automation services.
public sealed class NeuronTestKernelConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryStreams("Default")
            .AddMemoryStreams("HomeFeed")
            .AddMemoryStreams("DigitalBrainTimeline")
            .AddMemoryGrainStorage("PubSubStore")
            .ConfigureServices(services =>
            {
                services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddScoped<NeuronJournals>();
                services.Configure<NeuronLifecycleOptions>(options => options.JournalActivationMarkers = true);
                services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationDefinitionApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationRemovalApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, FoundryRunApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, FoundryDeployApplyHandler>();
                services.AddSingleton<ICapabilityBroker, CapabilityBroker>();
                services.AddSingleton<IScopedChatClientFactory, NoOpScopedChatClientFactory>();
                services.AddSingleton<IInoCapabilityRecall, DigitalBrain.Ino.InoCapabilityRecall>();
                services.AddSingleton<IInoToolProvider, DigitalBrain.Google.GmailInoToolProvider>();
                services.AddSingleton<IInoToolProvider, DigitalBrain.Salesforce.SalesforceInoToolProvider>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                services.AddSingleton<DocumentIngestor>();
                services.AddSingleton<SqliteSchemaInspector>();
                services.AddSingleton<HomeFeedBus>();
                services.AddSingleton<SignalEgressBus>();
                services.AddSignalEgressStreamSubscriber();
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["DigitalBrain:Automations:Enabled"] = "true"
                        })
                        .Build());
            });
        siloBuilder.AddFoundry();
    }
}

