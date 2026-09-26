using DigitalBrain.Core;
using DigitalBrain.Qdrant.Query;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Qdrant.Client;

namespace DigitalBrain.Qdrant;

[ModuleConfiguration(typeof(QdrantConfigurationContract))]
[ModuleHosting("DigitalBrain.Qdrant.Aspire.Hosting.QdrantModuleHosting, DigitalBrain.Modules.Qdrant.Aspire.Hosting")]
public sealed class QdrantModule : IModule
{
    public const string ConfigurationRoot = QdrantModuleOptions.SectionName;
    public const string ProviderConfigurationKey = QdrantModuleOptions.SectionName + ":Provider";
    public const string ProviderName = "Qdrant";

    public static ModuleDefinition Define(QdrantModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(QdrantModule), new Dictionary<string, string?>
        {
            [QdrantModuleOptions.SectionName + ":Provider"] = options.Provider,
            [QdrantModuleOptions.SectionName + ":ConnectionName"] = options.ConnectionName,
            [QdrantModuleOptions.SectionName + ":CollectionName"] = options.CollectionName,
            [QdrantModuleOptions.SectionName + ":VectorSize"] = options.VectorSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<QdrantModuleOptions>()
            .BindConfiguration(QdrantModuleOptions.SectionName)
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, ProviderName, StringComparison.OrdinalIgnoreCase),
                "Qdrant requires DigitalBrain:Qdrant:Provider=Qdrant.")
            .Validate(static options => QdrantPointRules.IsCollectionName(options.CollectionName),
                "Qdrant requires a collection name of letters, digits, underscores or hyphens.")
            .Validate(static options => options.VectorSize is >= 1 and <= QdrantPointRules.MaxVectorSize,
                "Qdrant requires DigitalBrain:Qdrant:VectorSize between 1 and 4096.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString)
                && QdrantRegistration.TryParseConnectionString(options.ConnectionString, out _, out _),
                "Qdrant requires a connection string with Endpoint and an optional Key.");
        services.TryAddSingleton(CreateClient);
        services.TryAddSingleton<IQdrantProvider>(CreateProvider);
        services.AddHealthChecks().AddCheck<QdrantHealthCheck>("qdrant", tags: ["ready"]);
    }

    private static QdrantClient CreateClient(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<QdrantModuleOptions>>().Value;
        return QdrantRegistration.CreateClient(options.ConnectionString!);
    }

    private static QdrantClientProvider CreateProvider(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<QdrantModuleOptions>>().Value;
        return new QdrantClientProvider(services.GetRequiredService<QdrantClient>(), options.CollectionName, options.VectorSize);
    }
}