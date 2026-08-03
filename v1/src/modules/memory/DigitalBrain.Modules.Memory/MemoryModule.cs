using DigitalBrain.Abstractions;
using DigitalBrain.Memory.Qdrant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qdrant.Client;

namespace DigitalBrain.Memory;

public sealed partial class MemoryModule : IModule
{
    public const string ProviderConfigurationKey = "DigitalBrain:Memory:Provider";
    public const string QdrantProviderName = "Qdrant";

    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        var provider = builder.Configuration[ProviderConfigurationKey];
        if (string.Equals(provider, QdrantProviderName, StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.TryAddSingleton(CreateQdrantClient);
            builder.Services.TryAddSingleton(CreateQdrantProvider);
            builder.Services.AddSingleton<IVectorMemoryStore, QdrantVectorMemoryStore>();
        }
        else
        {
            var connectionName = ResolveQdrantConnectionName(builder.Configuration);
            var qdrantConnectionString = builder.Configuration.GetConnectionString(connectionName)
                ?? builder.Configuration[$"ConnectionStrings:{connectionName}"];
            if (!string.IsNullOrWhiteSpace(qdrantConnectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{connectionName}' is configured but '{ProviderConfigurationKey}' is " +
                    (string.IsNullOrWhiteSpace(provider) ? "unset" : $"'{provider}', not '{QdrantProviderName}'") +
                    $"; memory would silently fall back to an in-memory store and lose data on restart. Set " +
                    $"'{ProviderConfigurationKey}' to '{QdrantProviderName}' to use it, or remove connection " +
                    $"string '{connectionName}' to genuinely opt into in-memory storage.");
            }

            builder.Services.TryAddSingleton<IVectorMemoryStore, InMemoryVectorMemoryStore>();
        }

        builder.Services.TryAddSingleton(static services =>
            new ProjectionReconciler(
                services.GetRequiredService<IVectorMemoryStore>(),
                services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()));
    }

    private static string ResolveQdrantConnectionName(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var connectionName = configuration[QdrantVectorMemoryRegistration.ConnectionNameConfigurationKey];
        return string.IsNullOrWhiteSpace(connectionName)
            ? QdrantVectorMemoryRegistration.DefaultConnectionName
            : connectionName;
    }

    private static QdrantClient CreateQdrantClient(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var connectionName = ResolveQdrantConnectionName(configuration);
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? configuration[$"ConnectionStrings:{connectionName}"]
            ?? throw new InvalidOperationException(
                $"Qdrant vector memory requires connection string '{connectionName}'.");

        return QdrantVectorMemoryRegistration.CreateClient(connectionString);
    }

    private static QdrantVectorMemoryProvider CreateQdrantProvider(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var client = services.GetRequiredService<QdrantClient>();
        var collectionName = configuration[QdrantVectorMemoryRegistration.CollectionNameConfigurationKey];
        return new QdrantVectorMemoryProvider(client, collectionName);
    }
}
