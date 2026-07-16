using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.Memory;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;
namespace DigitalBrain.Kernel.Hosting;

internal static class DigitalBrainOrleansExtensions
{
    public static IHostApplicationBuilder UseDigitalBrainOrleans(this IHostApplicationBuilder builder)
    {
        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted(builder.Configuration);
        var requiresDurableStorage = isAspireHosted || builder.Environment.IsProduction();
        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);
        var runtimeStorageNamespace = RuntimeStateNamespace.Resolve(builder.Configuration);
        var keyRing = RuntimeStateKeyConfiguration.Load(builder.Configuration, requireConfiguredKeys: requiresDurableStorage, production: builder.Environment.IsProduction());
        var stateProtector = new EncryptedRuntimeStateProtector(keyRing);
        builder.Services.AddSingleton(keyRing);
        builder.Services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
        builder.Services.AddSingleton(stateProtector);
        builder.Services.AddSingleton<InoEffectPlanAuthority>();
        builder.Services.AddSingleton<IFeatureGrainResolver, OrleansFeatureGrainResolver>();
        builder.Services.AddSingleton(new RuntimeStateHealthMetadata(
            requiresDurableStorage ? useManagedIdentity ? "azure-blob-managed-identity" : "azure-blob-connection-string" : "memory",
            runtimeStorageNamespace,
            RuntimeStateSchemas.Envelope,
            keyRing.ActiveKekVersion));
        builder.Services.AddHealthChecks().AddCheck<RuntimeStateHealthCheck>("digitalbrain-runtime-state");
        var storageCredential = useManagedIdentity ? new DefaultAzureCredential() : null;
        var storageTableServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.table.core.windows.net") : null;
        var storageBlobServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.blob.core.windows.net") : null;
        var clusteringConnection = requiresDurableStorage && !useManagedIdentity ? RequireConnectionString(builder.Configuration, "clustering") : null;
        var grainStateConnection = requiresDurableStorage && !useManagedIdentity ? RequireConnectionString(builder.Configuration, "grainstate") : null;
        var runtimeBlobOptions = new BlobClientOptions { Diagnostics = { IsDistributedTracingEnabled = false } };
        var runtimeStateBlobs = requiresDurableStorage
            ? useManagedIdentity
                ? new BlobServiceClient(storageBlobServiceUri!, storageCredential!, runtimeBlobOptions)
                : new BlobServiceClient(grainStateConnection!, runtimeBlobOptions)
            : null;
        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<IInoEffectPlanStore, InoEffectPlanStore>();
                if (builder.Configuration.GetValue<bool>("DigitalBrain:Tools:Enabled"))
                    services.AddSingleton<IInoEffectExecutor, InoEffectExecutor>();
                else
                    services.AddSingleton<IInoEffectExecutor, DisabledInoEffectExecutor>();
            });
            if (!requiresDurableStorage)
            {
                siloBuilder.UseLocalhostClustering();
                siloBuilder.UseInMemoryReminderService();
                siloBuilder.AddMemoryGrainStorageAsDefault();
                siloBuilder.AddMemoryGrainStorage(RuntimeStateStorageProviders.Conversations);
                siloBuilder.AddMemoryGrainStorage(RuntimeStateStorageProviders.SurfaceFeeds);
                siloBuilder.AddMemoryGrainStorage(RuntimeStateStorageProviders.Sessions);
            }
            else
            {
                var clusterId = builder.Configuration["Orleans:ClusterId"] ?? "digitalbrain";
                var serviceId = builder.Configuration["Orleans:ServiceId"] ?? "digitalbrain";
                siloBuilder.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = clusterId;
                    options.ServiceId = serviceId;
                });
                if (useManagedIdentity)
                {
                    var tableOptions = new TableClientOptions { Diagnostics = { IsDistributedTracingEnabled = false } };
                    siloBuilder.UseAzureStorageClustering(options =>
                        options.TableServiceClient = new TableServiceClient(storageTableServiceUri!, storageCredential!, tableOptions));
                    siloBuilder.UseAzureTableReminderService(options =>
                    {
                        options.TableServiceClient = new TableServiceClient(storageTableServiceUri!, storageCredential!, tableOptions);
                        options.TableName = "OrleansReminders";
                    });
                    var blobs = runtimeStateBlobs!;
                    ConfigureDefaultStorage(siloBuilder, blobs);
                    ConfigureRuntimeStateStorage(siloBuilder, blobs, runtimeStorageNamespace);
                }
                else
                {
                    var tableOptions = new TableClientOptions { Diagnostics = { IsDistributedTracingEnabled = false } };
                    siloBuilder.UseAzureStorageClustering(options =>
                        options.TableServiceClient = new TableServiceClient(clusteringConnection!, tableOptions));
                    siloBuilder.UseAzureTableReminderService(options =>
                    {
                        options.TableServiceClient = new TableServiceClient(clusteringConnection!, tableOptions);
                        options.TableName = "OrleansReminders";
                    });
                    var grainStateBlobs = runtimeStateBlobs!;
                    ConfigureDefaultStorage(siloBuilder, grainStateBlobs);
                    ConfigureRuntimeStateStorage(siloBuilder, grainStateBlobs, runtimeStorageNamespace);
                }
            }
        });
        return builder;
    }
    private static void ConfigureDefaultStorage(ISiloBuilder siloBuilder, BlobServiceClient blobs) =>
        siloBuilder.AddAzureBlobGrainStorage(
            "Default",
            (OptionsBuilder<AzureBlobStorageOptions> builder) => builder.Configure<OrleansJsonSerializer>((options, serializer) =>
            {
                options.BlobServiceClient = blobs;
                options.GrainStorageSerializer = new FeatureHubStateStorageSerializer(new JsonGrainStorageSerializer(serializer));
            }));
    private static void ConfigureRuntimeStateStorage(ISiloBuilder siloBuilder, BlobServiceClient blobs, string storageNamespace)
    {
        Add(RuntimeStateStorageProviders.Conversations);
        Add(RuntimeStateStorageProviders.SurfaceFeeds);
        Add(RuntimeStateStorageProviders.Sessions);
        return;
        void Add(string providerName) => siloBuilder.AddAzureBlobGrainStorage(providerName, options =>
        {
            options.BlobServiceClient = blobs;
            options.ContainerName = RuntimeStateNamespace.Container(
                storageNamespace,
                providerName switch
                {
                    RuntimeStateStorageProviders.Conversations => "conversations",
                    RuntimeStateStorageProviders.SurfaceFeeds => "surface-feeds",
                    RuntimeStateStorageProviders.Sessions => "sessions",
                    _ => throw new InvalidOperationException("Unknown runtime-state provider.")
                });
        });
    }
    private static string RequireConnectionString(IConfiguration configuration, string name)
    {
        var value = configuration.GetConnectionString(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"ConnectionStrings:{name} is required for hosted or Production Orleans storage.");
    }
    public static IHostApplicationBuilder AddDigitalBrainClients(this IHostApplicationBuilder builder)
    {
        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted(builder.Configuration);
        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);
        var storageCredential = useManagedIdentity ? new DefaultAzureCredential() : null;
        var storageTableServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.table.core.windows.net") : null;
        var storageBlobServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.blob.core.windows.net") : null;
        if (isAspireHosted && !useManagedIdentity)
        {
            var clusteringServiceKey = builder.Configuration["Orleans:Clustering:ServiceKey"] ?? "clustering";
            var grainStorageServiceKeys = new[]
            {
                builder.Configuration["Orleans:GrainStorage:Default:ServiceKey"] ?? "grainstate",
                builder.Configuration[$"Orleans:GrainStorage:{RuntimeStateStorageProviders.Conversations}:ServiceKey"] ?? "conversationstate",
                builder.Configuration[$"Orleans:GrainStorage:{RuntimeStateStorageProviders.SurfaceFeeds}:ServiceKey"] ?? "surfacefeedstate",
                builder.Configuration[$"Orleans:GrainStorage:{RuntimeStateStorageProviders.Sessions}:ServiceKey"] ?? "sessionstate"
            };
            builder.AddKeyedAzureTableServiceClient(clusteringServiceKey, settings => settings.DisableTracing = true);
            foreach (var serviceKey in grainStorageServiceKeys.Distinct(StringComparer.Ordinal))
                builder.AddKeyedAzureBlobServiceClient(serviceKey, settings => settings.DisableTracing = true);
            builder.AddAzureBlobServiceClient("grainstate", settings =>
            {
                settings.DisableHealthChecks = true;
                settings.DisableTracing = true;
            });
        }
        builder.Services.AddDigitalBrainChat(builder.Configuration, storageCredential);
        TableClient? memoryTable = null;
        if (useManagedIdentity)
        {
            var tableOptions = new TableClientOptions { Diagnostics = { IsDistributedTracingEnabled = false } };
            memoryTable = new TableClient(storageTableServiceUri!, AzureTableMemoryFactStore.FactsTableName, storageCredential!, tableOptions);
        }
        builder.Services.AddDigitalBrainMemory(builder.Configuration, memoryTable, builder.Environment.IsEnvironment("Testing") || builder.Configuration.GetValue<bool>("DigitalBrain:TestMode"));
        builder.Services.AddSingleton<CapabilityGrantValidator>();
        builder.Services.AddSingleton<FeatureCapabilityGrantSource>();
        builder.Services.AddSingleton<ICapabilityGrantSource>(services => services.GetRequiredService<FeatureCapabilityGrantSource>());
        builder.Services.AddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();
        builder.Services.TryAddSingleton<ICapabilityCatalog, BuiltInCapabilityCatalog>();
        builder.Services.TryAddSingleton<IOwnerConnectionHealth, OwnerConnectionHealth>();
        builder.Services.TryAddSingleton<IOwnerConnectionCatalog, OwnerConnectionCatalog>();
        builder.Services.TryAddSingleton<IFeatureCapabilityProjectionSource, FeatureCapabilityProjectionSource>();
        builder.Services.TryAddSingleton<IOwnerCapabilityCatalog, OwnerCapabilityCatalog>();
        builder.Services.TryAddSingleton<ICapabilityResolver, HybridCapabilityResolver>();
        builder.Services.TryAddSingleton<ICapabilityParameterModel, CapabilityParameterModel>();
        builder.Services.TryAddSingleton<IFeatureRunGateway, FeatureRunGateway>();
        builder.Services.TryAddSingleton<IFeatureCapabilityInvoker, FeatureCapabilityInvoker>();
        builder.Services.AddHostedService<CapabilityDispatcherStartupValidation>();
        builder.Services.AddSingleton<IAgentWorkflowRunner, AgentFrameworkWorkflowRunner>();
        BlobServiceClient? integrationConfigBlobs = null;
        if (isAspireHosted)
        {
            var blobOptions = new BlobClientOptions();
            blobOptions.Diagnostics.IsDistributedTracingEnabled = false;
            if (useManagedIdentity)
            {
                integrationConfigBlobs = new BlobServiceClient(storageBlobServiceUri!, storageCredential!, blobOptions);
            }
            else
            {
                var grainStateConnStr = builder.Configuration.GetConnectionString("grainstate");
                if (!string.IsNullOrEmpty(grainStateConnStr))
                {
                    integrationConfigBlobs = new BlobServiceClient(grainStateConnStr, blobOptions);
                }
            }
        }
        builder.Services.AddIntegrationConfigStore(integrationConfigBlobs);
        builder.Services.AddSingleton<DigitalBrain.Kernel.Contracts.IOAuthStateProtector, DataProtectionOAuthStateProtector>();
        return builder;
    }
}
