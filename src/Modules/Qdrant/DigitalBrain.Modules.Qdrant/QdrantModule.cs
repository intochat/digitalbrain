using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Qdrant;

[ModuleDeployment("DigitalBrain.Qdrant.QdrantDeployment, DigitalBrain.Modules.Qdrant.Deployment")]
public sealed class QdrantModule : IModule<QdrantModuleOptions>
{
    public const string ConnectionName = "qdrant";

    public static bool IsConnected(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionName));

    // Without a connection IQdrant is served from memory, so search still works; nothing survives a restart.
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        if (silo.Configuration.GetConnectionString(ConnectionName) is { Length: > 0 } connectionString)
        {
            silo.Services.TryAddSingleton(_ => QdrantConnection.CreateClient(connectionString));
            silo.Services.TryAddSingleton<IQdrant, QdrantStore>();
        }
        else
        {
            silo.Services.TryAddSingleton<IQdrant, InMemoryQdrant>();
        }
    }
}

public sealed class QdrantModuleOptions : IModuleOptions
{
    public bool Host { get; set; }

    public QdrantModuleOptions WithHostedQdrant()
    {
        Host = true;
        return this;
    }

    public void Validate() { }
}
