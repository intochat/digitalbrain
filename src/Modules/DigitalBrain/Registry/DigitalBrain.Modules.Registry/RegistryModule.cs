using DigitalBrain.Sdk.Vectors;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Registry;

public sealed class RegistryModule : IModule
{
    public const string Key = IRegistry.Key;
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton<NeuronTypes>();
        silo.Services.TryAddSingleton(provider => new NeuronTypeSearch(provider.GetRequiredService<NeuronTypes>(),
            provider.GetService<IVectorStore>(), Embeddings(provider)));
        silo.Services.AddHostedService<RegistryObserver>();
    }

    private static IEmbeddingGenerator<string, Embedding<float>>? Embeddings(IServiceProvider services)
    {
        try { return services.GetService<IEmbeddingGenerator<string, Embedding<float>>>(); }
        catch (InvalidOperationException) { return null; } // An unconfigured AI provider must not disable the registry.
    }
}
