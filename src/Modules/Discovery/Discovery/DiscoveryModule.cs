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
        services.TryAddSingleton<IManifestSource, EmptyManifestSource>();
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
        var generator = services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        return generator is null ? new HashingCapabilityEmbedder() : new AiCapabilityEmbedder(generator);
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
