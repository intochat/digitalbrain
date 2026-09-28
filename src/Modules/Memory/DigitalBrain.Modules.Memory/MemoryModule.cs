using DigitalBrain.Core;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Memory;

[ModuleConfiguration(typeof(MemoryConfigurationContract))]
public sealed class MemoryModule : IModule
{
    public static ModuleDefinition Define(MemoryModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(MemoryModule), new Dictionary<string, string?>
        {
            [MemoryModuleOptions.SectionName + ":CollectionName"] = options.CollectionName,
        });
    }

    // Canonical memory lives in neurons; the vector projection exists only over a real Qdrant connection.
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        if (!QdrantModule.IsConnected(silo.Configuration)) { return; }
        var collectionName = silo.Configuration[MemoryModuleOptions.SectionName + ":CollectionName"];
        services.TryAddSingleton(provider => new VectorMemoryStore(provider.GetRequiredService<IQdrant>(), collectionName));
        services.TryAddSingleton<IVectorMemoryStore>(provider => provider.GetRequiredService<VectorMemoryStore>());
        services.TryAddSingleton<ILegacyVectorMemoryStore>(provider => provider.GetRequiredService<VectorMemoryStore>());
    }
}
