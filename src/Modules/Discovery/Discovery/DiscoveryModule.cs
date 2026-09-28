using DigitalBrain.Core;
using DigitalBrain.Discovery.Sources;
using DigitalBrain.Qdrant;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;

namespace DigitalBrain.Discovery;

// Requires the Qdrant module. Other modules publish what they offer as ICapabilitySource.
[ModuleConfiguration(typeof(DiscoveryConfigurationContract))]
public sealed class DiscoveryModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(DiscoveryModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilitySource, NeuronCapabilitySource>());
        services.TryAddSingleton(provider => new CapabilityCatalog(
            provider.GetServices<ICapabilitySource>(),
            provider.GetRequiredService<IQdrant>(),
            Embeddings(provider),
            provider.GetRequiredService<ILogger<CapabilityCatalog>>()));
        services.AddHostedService<CapabilityCatalogRebuilder>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapDiscoveryBoard();
    }

    private static IEmbeddingGenerator<string, Embedding<float>>? Embeddings(IServiceProvider services)
    {
        try
        {
            return services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        }
        catch (InvalidOperationException)
        {
            // The AI module registers a factory that throws when no embedding model is configured.
            return null;
        }
    }
}
