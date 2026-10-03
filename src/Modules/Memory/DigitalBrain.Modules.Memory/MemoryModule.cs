using DigitalBrain.Sdk.Vectors;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Memory;

[ModuleId("memory")]
public sealed class MemoryModule : IModule<MemoryModuleOptions>
{
    // Canonical memory lives in neurons; a composed vector store supplies the optional projection.
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        var collectionName = silo.Configuration.GetModuleOptions<MemoryModuleOptions>("memory").CollectionName;
        services.TryAddSingleton<IVectorMemoryStore>(provider => provider.GetService<IVectorStore>() is { } vectors
            ? new VectorMemoryStore(vectors, collectionName) : null!);
    }
}
