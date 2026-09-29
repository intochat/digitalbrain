using DigitalBrain.Core;
using DigitalBrain.Registry.Configuration;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Registry;

[ModuleConfiguration(typeof(RegistryConfigurationContract))]
public sealed class RegistryModule : IModule
{
    public const string Key = "registry";
    public static ModuleDefinition Define() => new(typeof(RegistryModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton<NeuronTypes>();
        silo.Services.TryAddSingleton(provider => new NeuronTypeSearch(provider.GetRequiredService<NeuronTypes>(),
            provider.GetService<IQdrant>(), Embeddings(provider)));
        silo.Services.AddHostedService<RegistryObserver>();
    }

    private static IEmbeddingGenerator<string, Embedding<float>>? Embeddings(IServiceProvider services)
    {
        try { return services.GetService<IEmbeddingGenerator<string, Embedding<float>>>(); }
        catch (InvalidOperationException) { return null; } // An unconfigured AI provider must not disable the registry.
    }
}