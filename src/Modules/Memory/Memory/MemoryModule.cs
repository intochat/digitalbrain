using DigitalBrain.Memory.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace DigitalBrain.Memory;

public sealed class MemoryModule : Core.IModule
{
    public const string ConfigurationRoot = MemoryOptions.SectionName;
    public const string ProviderConfigurationKey = "DigitalBrain:Memory:Provider";
    public const string QdrantProviderName = "Qdrant";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<MemoryOptions>()
            .BindConfiguration(ConfigurationRoot)
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, QdrantProviderName, StringComparison.OrdinalIgnoreCase),
                "Memory requires DigitalBrain:Memory:Provider=Qdrant.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.Qdrant.ConnectionString)
                && QdrantVectorMemoryRegistration.TryParseConnectionString(options.Qdrant.ConnectionString, out _, out _),
                "Qdrant vector memory requires a valid connection string with Endpoint and optional Key.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton(CreateQdrantClient);
        builder.Services.TryAddSingleton(CreateQdrantProvider);
        builder.Services.AddSingleton<IVectorMemoryStore, QdrantVectorMemoryStore>();
    }

    private static QdrantClient CreateQdrantClient(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MemoryOptions>>().Value.Qdrant;
        var connectionName = options.ConnectionName;
        var connectionString = options.ConnectionString
            ?? throw new InvalidOperationException(
                $"Qdrant vector memory requires connection string '{connectionName}'.");

        return QdrantVectorMemoryRegistration.CreateClient(connectionString);
    }

    private static QdrantVectorMemoryProvider CreateQdrantProvider(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MemoryOptions>>().Value;
        var client = services.GetRequiredService<QdrantClient>();
        var collectionName = options.Qdrant.CollectionName;
        return new QdrantVectorMemoryProvider(client, collectionName);
    }
}
