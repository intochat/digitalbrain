using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Qdrant;

[ModuleDeployment("DigitalBrain.Qdrant.QdrantDeployment, DigitalBrain.Modules.Qdrant.Deployment")]
[ModuleConfiguration(typeof(QdrantConfigurationContract))]
[ModuleHosting("DigitalBrain.Qdrant.Aspire.Hosting.QdrantModuleHosting, DigitalBrain.Modules.Qdrant.Aspire.Hosting")]
public sealed class QdrantModule : IModule
{
    public const string ConnectionName = "qdrant";

    public static ModuleDefinition Define(QdrantModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(QdrantModule), new Dictionary<string, string?>
        {
            [QdrantModuleOptions.SectionName + ":Host"] = options.Host.ToString(),
        });
    }

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

public sealed class QdrantModuleOptions
{
    public const string SectionName = "DigitalBrain:Qdrant";

    public bool Host { get; set; }
}

public sealed class QdrantConfigurationContract() : ModuleConfigurationContract<QdrantModule, QdrantModuleOptions>("Host")
{
    protected override ModuleDefinition Compile(QdrantModuleOptions options) => QdrantModule.Define(options);
}

public static class QdrantModuleConfiguration
{
    public static ModuleConfiguration<QdrantModule> WithHostedQdrant(this ModuleConfiguration<QdrantModule> module)
    {
        module.ConfigureOptions<QdrantModuleOptions>(options => options.Host = true, "Host");
        return module;
    }
}
