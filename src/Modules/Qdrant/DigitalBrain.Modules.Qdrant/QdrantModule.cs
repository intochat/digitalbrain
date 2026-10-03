using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Sdk.Vectors;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Qdrant;

[ModuleDeployment("DigitalBrain.Qdrant.QdrantDeployment, DigitalBrain.Modules.Qdrant.Deployment")]
[ModuleId("qdrant")]
public sealed class QdrantModule : IModule<QdrantModuleOptions>
{
    public const string ConnectionName = "qdrant";

    public static bool IsConnected(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionName));

    // Without a connection IVectorStore is served from memory, so search still works; nothing survives a restart.
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton(_ => QdrantConnection.CreateClient(
            silo.Configuration.GetConnectionString(ConnectionName)!));
        silo.Services.TryAddSingleton<IVectorStore>(provider => IsConnected(silo.Configuration)
            ? new QdrantStore(provider.GetRequiredService<global::Qdrant.Client.QdrantClient>()) : new InMemoryQdrant());
    }
}
