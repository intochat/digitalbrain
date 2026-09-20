using DigitalBrain.Core;
using DigitalBrain.Memory.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Qdrant.Client;

namespace DigitalBrain.Memory;

public sealed class MemoryModule : IModule
{
    public const string ProviderConfigurationKey = "DigitalBrain:Memory:Provider";
    public const string QdrantProviderName = "Qdrant";

    public static ModuleDefinition Define(MemoryModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(MemoryModule), new Dictionary<string, string?>
        {
            [MemoryModuleOptions.SectionName + ":Provider"] = options.Provider,
            [MemoryModuleOptions.SectionName + ":Qdrant:ConnectionName"] = options.Qdrant.ConnectionName,
            [MemoryModuleOptions.SectionName + ":Qdrant:CollectionName"] = options.Qdrant.CollectionName,
        });
    }

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<MemoryModuleOptions>()
            .Bind(silo.Configuration.GetSection(MemoryModuleOptions.SectionName))
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, QdrantProviderName, StringComparison.OrdinalIgnoreCase),
                "Memory requires DigitalBrain:Memory:Provider=Qdrant.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.Qdrant.ConnectionString)
                && QdrantVectorMemoryRegistration.TryParseConnectionString(options.Qdrant.ConnectionString, out _, out _),
                "Qdrant vector memory requires a valid connection string with Endpoint and optional Key.");
        services.TryAddSingleton(CreateQdrantClient);
        services.TryAddSingleton(CreateQdrantProvider);
        services.TryAddSingleton<IVectorMemoryStore, QdrantVectorMemoryStore>();
    }

    private static QdrantClient CreateQdrantClient(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MemoryModuleOptions>>().Value.Qdrant;
        var connectionName = options.ConnectionName;
        var connectionString = options.ConnectionString
            ?? throw new InvalidOperationException(
                $"Qdrant vector memory requires connection string '{connectionName}'.");

        return QdrantVectorMemoryRegistration.CreateClient(connectionString);
    }

    private static QdrantVectorMemoryProvider CreateQdrantProvider(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MemoryModuleOptions>>().Value;
        var client = services.GetRequiredService<QdrantClient>();
        var collectionName = options.Qdrant.CollectionName;
        return new QdrantVectorMemoryProvider(client, collectionName);
    }
}
