using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
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

public sealed class NeuronTestKernelConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryStreams("Default")
            .AddMemoryStreams("DigitalBrainTimeline")
            .AddMemoryGrainStorage("PubSubStore")
            .ConfigureServices(services =>
            {
                services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
                services.AddScoped<NeuronJournals>();
                services.Configure<NeuronLifecycleOptions>(options => options.JournalActivationMarkers = true);
                services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
                services.AddSingleton<IScopedChatClientFactory, NoOpScopedChatClientFactory>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
            });
    }
}

