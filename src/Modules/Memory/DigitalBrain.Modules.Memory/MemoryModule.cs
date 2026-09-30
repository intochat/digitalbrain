using DigitalBrain.Core;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Memory;

public sealed class MemoryModule : IModule<MemoryModuleOptions>
{
    // Canonical memory lives in neurons; the vector projection exists only over a real Qdrant connection.
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        if (!QdrantModule.IsConnected(silo.Configuration)) { return; }
        var collectionName = silo.Configuration.GetModuleOptions<MemoryModuleOptions>(nameof(MemoryModule)).CollectionName;
        services.TryAddSingleton(provider => new VectorMemoryStore(provider.GetRequiredService<IQdrant>(), collectionName));
        services.TryAddSingleton<IVectorMemoryStore>(provider => provider.GetRequiredService<VectorMemoryStore>());
        services.TryAddSingleton<ILegacyVectorMemoryStore>(provider => provider.GetRequiredService<VectorMemoryStore>());
    }
}
