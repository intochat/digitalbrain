using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
                AddRuntimeStateProtection(services);
                services.Configure<NeuronLifecycleOptions>(options => options.PersistActivationMarkers = true);
                services.AddSingleton<IScopedChatClientFactory, NoOpScopedChatClientFactory>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
            });
    }

    public static void AddRuntimeStateProtection(IServiceCollection services)
    {
        var keyRing = new RuntimeStateKeyRing(
            1,
            new Dictionary<int, byte[]> { [1] = Enumerable.Repeat((byte)0x31, 32).ToArray() },
            Enumerable.Repeat((byte)0x53, 32).ToArray());
        services.AddSingleton(keyRing);
        services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
        services.AddSingleton(new EncryptedRuntimeStateProtector(keyRing, SynapseJson.CreateOptions()));
    }
}

