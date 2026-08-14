using Brain.Abstractions.Policy;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Core.Modules;
using Brain.Core.Policy;
using Brain.Product.Abstractions.Authority;
using Azure.Data.Tables;
using DigitalBrain.ProductHost.Authority;
using DigitalBrain.ProductHost.Catalog;
using DigitalBrain.ProductHost.Persistence;
using DigitalBrain.ProductHost.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.ProductHost.Hosting;

public interface IDurableOrleansConfiguration;

internal sealed class DurableOrleansConfiguration : IDurableOrleansConfiguration;

public static class ProductHostServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainProductHost(
        this IHostApplicationBuilder builder,
        Action<ProductStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var mutableOptions = new ProductStoreOptions();
        builder.Configuration.GetSection(ProductStoreOptions.SectionName).Bind(mutableOptions);
        configure?.Invoke(mutableOptions);
        ApplyDevelopmentDefaults(builder.Environment, mutableOptions);
        var configured = Snapshot(mutableOptions);

        builder.Services
            .AddOptions<ProductStoreOptions>()
            .Configure(options => Copy(configured, options))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ProductStoreOptions>, ProductStoreOptionsValidator>();

        RegisterPersistence(builder.Services, configured);
        RegisterSecretStorage(builder.Services, builder.Environment, configured);
        RegisterCoreAndProductRuntime(builder.Services);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ProductHostStartupValidator>());
        RegisterOrleans(builder, configured);
        return builder;
    }

    private static void RegisterPersistence(
        IServiceCollection services,
        ProductStoreOptions configured)
    {
        services.AddDbContext<ProductDbContext>(options =>
        {
            if (configured.Persistence == ProductPersistenceKind.InMemory)
            {
                options.UseInMemoryDatabase("digitalbrain-product-development");
                return;
            }

            options.UseNpgsql(configured.PostgreSqlConnectionString
                ?? "Host=configuration-required;Database=digitalbrain");
        });
    }

    private static void RegisterSecretStorage(
        IServiceCollection services,
        IHostEnvironment environment,
        ProductStoreOptions configured)
    {
        if (environment.IsDevelopment()
            && configured.ObjectStorage == ProductObjectStorageKind.InMemory)
        {
            services.TryAddSingleton<IEncryptedSecretObjectStore, DevelopmentSecretObjectStore>();
            services.TryAddSingleton<IKeyEncryptionProvider>(_ =>
                new DevelopmentKeyEncryptionProvider(configured.ObjectStoreEncryptionKeyId!));
        }

#if DEBUG
        if (environment.IsDevelopment() && configured.Authority == ProductAuthorityKind.LocalTest)
        {
            services.TryAddSingleton<IBrainAccessAuthority>(static _ => new LocalTestAuthority());
        }
#endif

        services.TryAddSingleton<EncryptedObjectSecretStore>();
        services.TryAddSingleton<ISecretStore>(static provider =>
            provider.GetRequiredService<EncryptedObjectSecretStore>());
        services.TryAddSingleton<IProviderSecretReader>(static provider =>
            provider.GetRequiredService<EncryptedObjectSecretStore>());
    }

    private static void RegisterCoreAndProductRuntime(IServiceCollection services)
    {
        services.TryAddSingleton<IModuleRegistry>(static provider =>
        {
            var registry = new ModuleRegistry();
            registry.Resolve(provider.GetServices<ModuleManifest>().ToArray());
            return registry;
        });
        services.TryAddSingleton(static provider =>
            provider.GetRequiredService<IModuleRegistry>().GetSnapshot().Modules);
        services.TryAddSingleton<IWorkspacePolicyEvaluator, WorkspacePolicyEvaluator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IWorkspacePolicyVersionProvider, RejectingPolicyVersionProvider>();
        services.TryAddSingleton<ProductOperationPolicyFilter>();
        services.TryAddSingleton(static provider => new ProductOperationCatalog(
            provider.GetRequiredService<IModuleRegistry>(),
            provider.GetRequiredService<ProductOperationPolicyFilter>(),
            provider.GetServices<ProductOperationRegistration>().ToArray()));
        services.TryAddScoped<IProductActivityProjectionService, DurableProductActivityProjectionService>();
        services.TryAddScoped<IOperationGateway, DurableProductOperationGateway>();
        services.TryAddScoped<IDurableProductDeliveryService, DurableProductDeliveryService>();
    }

    private static void RegisterOrleans(
        IHostApplicationBuilder builder,
        ProductStoreOptions configured)
    {
        if (configured.OrleansStorage == ProductOrleansStorageKind.InMemory)
        {
            builder.UseOrleans(static silo => silo
                .UseLocalhostClustering()
                .AddMemoryGrainStorageAsDefault(static (Orleans.Configuration.MemoryGrainStorageOptions _) => { })
                .UseInMemoryReminderService());
            return;
        }

        var clustering = configured.OrleansClusteringConnectionString ?? "UseDevelopmentStorage=true";
        var grains = configured.OrleansGrainStorageConnectionString ?? "UseDevelopmentStorage=true";
        var reminders = configured.OrleansReminderConnectionString ?? "UseDevelopmentStorage=true";
        builder.UseOrleans(silo => silo
            .UseAzureStorageClustering(options => options.TableServiceClient = new TableServiceClient(clustering))
            .AddAzureTableGrainStorageAsDefault(options => options.TableServiceClient = new TableServiceClient(grains))
            .UseAzureTableReminderService(options => options.TableServiceClient = new TableServiceClient(reminders)));
        builder.Services.TryAddSingleton<IDurableOrleansConfiguration, DurableOrleansConfiguration>();
    }

    private static void ApplyDevelopmentDefaults(
        IHostEnvironment environment,
        ProductStoreOptions configured)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        if (configured.ObjectStorage == ProductObjectStorageKind.InMemory)
        {
            configured.ObjectStoreBucket ??= "digitalbrain-development-secrets";
            configured.ObjectStoreEncryptionKeyId ??= "digitalbrain-development-key";
        }
    }

    private static void Copy(ProductStoreOptions source, ProductStoreOptions target)
    {
        target.Persistence = source.Persistence;
        target.Authority = source.Authority;
        target.ObjectStorage = source.ObjectStorage;
        target.OrleansStorage = source.OrleansStorage;
        target.PostgreSqlConnectionString = source.PostgreSqlConnectionString;
        target.ObjectStoreBucket = source.ObjectStoreBucket;
        target.ObjectStoreEncryptionKeyId = source.ObjectStoreEncryptionKeyId;
        target.OrleansClusteringConnectionString = source.OrleansClusteringConnectionString;
        target.OrleansGrainStorageConnectionString = source.OrleansGrainStorageConnectionString;
        target.OrleansReminderConnectionString = source.OrleansReminderConnectionString;
    }

    private static ProductStoreOptions Snapshot(ProductStoreOptions source)
    {
        var snapshot = new ProductStoreOptions();
        Copy(source, snapshot);
        return snapshot;
    }

    private sealed class RejectingPolicyVersionProvider : IWorkspacePolicyVersionProvider
    {
        public bool TryGetCurrentVersion(
            Brain.Abstractions.Identity.WorkspaceId workspace,
            out int policyVersion)
        {
            policyVersion = 0;
            return false;
        }
    }
}

internal sealed class ProductStoreOptionsValidator(
    IHostEnvironment environment,
    IServiceProviderIsService registeredServices)
    : IValidateOptions<ProductStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!Enum.IsDefined(options.Persistence)
            || !Enum.IsDefined(options.Authority)
            || !Enum.IsDefined(options.ObjectStorage)
            || !Enum.IsDefined(options.OrleansStorage))
        {
            failures.Add("ProductHost storage and authority modes must use declared values.");
        }

        if (environment.IsProduction())
        {
            Require(options.Persistence == ProductPersistenceKind.PostgreSql,
                "Production requires PostgreSQL persistence.", failures);
            Require(options.Authority == ProductAuthorityKind.External,
                "Production requires an externally supplied authority.", failures);
            Require(options.ObjectStorage == ProductObjectStorageKind.External,
                "Production requires external object storage.", failures);
            Require(options.OrleansStorage == ProductOrleansStorageKind.Durable,
                "Production requires durable Orleans grain and reminder storage.", failures);
        }
        else if (!environment.IsDevelopment()
            && (options.Persistence == ProductPersistenceKind.InMemory
                || options.Authority == ProductAuthorityKind.LocalTest
                || options.ObjectStorage == ProductObjectStorageKind.InMemory
                || options.OrleansStorage == ProductOrleansStorageKind.InMemory))
        {
            failures.Add("Local and in-memory implementations are available only in Development.");
        }

        if (options.Persistence == ProductPersistenceKind.PostgreSql)
        {
            RequireText(options.PostgreSqlConnectionString, nameof(options.PostgreSqlConnectionString), failures);
        }

        RequireText(options.ObjectStoreBucket, nameof(options.ObjectStoreBucket), failures);
        RequireText(options.ObjectStoreEncryptionKeyId, nameof(options.ObjectStoreEncryptionKeyId), failures);
        if (options.OrleansStorage == ProductOrleansStorageKind.Durable)
        {
            RequireText(options.OrleansClusteringConnectionString, nameof(options.OrleansClusteringConnectionString), failures);
            RequireText(options.OrleansGrainStorageConnectionString, nameof(options.OrleansGrainStorageConnectionString), failures);
            RequireText(options.OrleansReminderConnectionString, nameof(options.OrleansReminderConnectionString), failures);
        }

        RequireService<IBrainAccessAuthority>(registeredServices, failures);
        RequireService<IEncryptedSecretObjectStore>(registeredServices, failures);
        RequireService<IKeyEncryptionProvider>(registeredServices, failures);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(bool condition, string failure, ICollection<string> failures)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }

    private static void RequireText(string? value, string name, ICollection<string> failures)
        => Require(!string.IsNullOrWhiteSpace(value), $"{name} is required.", failures);

    private static void RequireService<T>(
        IServiceProviderIsService registeredServices,
        ICollection<string> failures)
    {
        if (!registeredServices.IsService(typeof(T)))
        {
            failures.Add($"A {typeof(T).Name} implementation is required.");
        }
    }
}

internal sealed class ProductHostStartupValidator(
    IOptions<ProductStoreOptions> options,
    ISecretStore secretStore) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = options.Value;
        _ = secretStore;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
