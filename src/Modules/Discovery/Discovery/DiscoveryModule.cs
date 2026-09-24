using DigitalBrain.Core;
using DigitalBrain.Discovery.Search;
using DigitalBrain.Discovery.Vector;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;
using Qdrant.Client;

namespace DigitalBrain.Discovery;

[ModuleConfiguration(typeof(DiscoveryConfigurationContract))]
public sealed class DiscoveryModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(DiscoveryModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IManifestSource, AppsManifestSource>());
        services.TryAddSingleton<ICapabilityEmbedder>(CreateEmbedder);
        services.TryAddSingleton<ICapabilityVectorIndex>(CreateVectorIndex);
        services.TryAddSingleton<CapabilityCatalog>();
        services.AddHostedService<CapabilityCatalogRebuilder>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapDiscoveryBoard();
    }

    private static ICapabilityEmbedder CreateEmbedder(IServiceProvider services)
    {
        try
        {
            var generator = services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
            return generator is null ? new HashingCapabilityEmbedder() : new AiCapabilityEmbedder(generator);
        }
        catch (InvalidOperationException)
        {
            // The AI module registers a factory that throws when no embedding model is configured;
            // discovery must still start and serve keyword search.
            return new HashingCapabilityEmbedder();
        }
    }

    private static ICapabilityVectorIndex CreateVectorIndex(IServiceProvider services)
    {
        var model = services.GetRequiredService<ICapabilityEmbedder>().ModelId;
        var client = services.GetService<QdrantClient>();
        return client is null
            ? new InMemoryCapabilityVectorIndex(model)
            : new QdrantCapabilityVectorIndex(client, model);
    }
}
