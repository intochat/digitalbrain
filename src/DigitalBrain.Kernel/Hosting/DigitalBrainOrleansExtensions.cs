using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Hosting;

public static class DigitalBrainOrleansExtensions
{
    public static IHostApplicationBuilder UseDigitalBrainOrleans(this IHostApplicationBuilder builder)
    {
        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted(builder.Configuration);
        var requiresDurableStorage = isAspireHosted || builder.Environment.IsProduction();
        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);
        var runtimeStorageNamespace = RuntimeStateNamespace.Resolve(builder.Configuration);
        var keyRing = RuntimeStateKeyConfiguration.Load(
            builder.Configuration,
            requireConfiguredKeys: requiresDurableStorage,
            production: builder.Environment.IsProduction());
        var stateProtector = new EncryptedRuntimeStateProtector(keyRing);

        builder.Services.AddSingleton(keyRing);
        builder.Services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
        builder.Services.AddSingleton(stateProtector);
        builder.Services.AddSingleton<InoEffectPlanAuthority>();
        builder.Services.AddSingleton(new RuntimeStateHealthMetadata(
            requiresDurableStorage
                ? useManagedIdentity ? "azure-blob-managed-identity" : "azure-blob-connection-string"
                : "memory",
            runtimeStorageNamespace,
            RuntimeStateSchemas.Envelope,
            keyRing.ActiveKekVersion));
        builder.Services.AddHealthChecks()
            .AddCheck<RuntimeStateHealthCheck>("digitalbrain-runtime-state");

        var storageCredential = useManagedIdentity ? new DefaultAzureCredential() : null;
        var storageTableServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.table.core.windows.net") : null;
        var storageBlobServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.blob.core.windows.net") : null;
        var clusteringConnection = requiresDurableStorage && !useManagedIdentity
            ? RequireConnectionString(builder.Configuration, "clustering")
            : null;
        var grainStateConnection = requiresDurableStorage && !useManagedIdentity
            ? RequireConnectionString(builder.Configuration, "grainstate")
            : null;
        var journalConnection = requiresDurableStorage && !useManagedIdentity
            ? RequireConnectionString(builder.Configuration, "journal")
            : null;

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
                services.AddScoped<NeuronJournals>();
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationDefinitionApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationRemovalApplyHandler>();
                services.AddSingleton<IInoEffectPlanStore, InoEffectPlanStore>();
                if (builder.Configuration.GetValue<bool>("DigitalBrain:Tools:Enabled"))
                    services.AddSingleton<IInoToolGateway, PlanInoToolGateway>();
                else
                    services.AddSingleton<IInoToolGateway, ClosedInoToolGateway>();
            });

            if (!requiresDurableStorage)
            {
                siloBuilder.UseLocalhostClustering();
                siloBuilder.UseInMemoryReminderService();
                siloBuilder.AddMemoryGrainStorageAsDefault();
                siloBuilder.AddMemoryGrainStorage(RuntimeStateStorageProviders.Conversations);
                siloBuilder.AddMemoryGrainStorage(RuntimeStateStorageProviders.SurfaceFeeds);
                siloBuilder.AddMemoryGrainStorage(RuntimeStateStorageProviders.Sessions);
                siloBuilder.ConfigurePrototypeJournals();
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
                    siloBuilder.AddAzureBlobGrainStorage("Default", options => options.BlobServiceClient = blobs);
                    ConfigureRuntimeStateStorage(siloBuilder, blobs, runtimeStorageNamespace);
                    siloBuilder.AddAzureBlobJournalStorage(options => options.BlobServiceClient = blobs);
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
                    siloBuilder.AddAzureBlobGrainStorage("Default", options => options.BlobServiceClient = grainStateBlobs);
                    ConfigureRuntimeStateStorage(siloBuilder, grainStateBlobs, runtimeStorageNamespace);
                    siloBuilder.AddAzureBlobJournalStorage(options =>
                    {
                        options.BlobServiceClient = new BlobServiceClient(journalConnection!, runtimeBlobOptions);
                    });
                }

                siloBuilder.UseJsonJournalFormat(options =>
                {
                    JournalJson.Configure(options);
                    options.SerializerOptions.Converters.Add(new EncryptedSynapseJsonConverter(
                        stateProtector,
                        RuntimeStateKeys.SynapseJournal(runtimeStorageNamespace),
                        EncryptedSynapseJsonConverter.DiscoverLoadedSynapseTypes()));
                });
            }

        });

        return builder;
    }

    private static void ConfigureRuntimeStateStorage(
        ISiloBuilder siloBuilder,
        BlobServiceClient blobs,
        string storageNamespace)
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
            : throw new InvalidOperationException(
                $"ConnectionStrings:{name} is required for hosted or Production Orleans storage.");
    }

    public static IHostApplicationBuilder AddDigitalBrainClients(this IHostApplicationBuilder builder)
    {
        var corsOrigins = builder.Configuration
            .GetSection("DigitalBrain:Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "https://digitalbrain.tech", "https://www.digitalbrain.tech" };

        builder.Services.AddCors(options => options.AddPolicy("browser", policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()));

        builder.Services.AddSingleton<ITelemetrySink, TelemetryBuffer>();
        builder.Services.AddSingleton(new SchemaRegistry([
            new SchemaDescriptor("digitalbrain.v2.command-envelope", 2, "Operational", true)]));
        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted(builder.Configuration);

        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);

        var storageCredential = useManagedIdentity ? new DefaultAzureCredential() : null;
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
        builder.Services.AddSingleton<IAgentWorkflowRunner, AgentFrameworkWorkflowRunner>();

        BlobServiceClient? packConfigBlobs = null;
        if (isAspireHosted)
        {
            var blobOptions = new BlobClientOptions();
            blobOptions.Diagnostics.IsDistributedTracingEnabled = false;

            if (useManagedIdentity)
            {
                packConfigBlobs = new BlobServiceClient(storageBlobServiceUri!, storageCredential!, blobOptions);
            }
            else
            {
                var grainStateConnStr = builder.Configuration.GetConnectionString("grainstate");
                if (!string.IsNullOrEmpty(grainStateConnStr))
                {
                    packConfigBlobs = new BlobServiceClient(grainStateConnStr, blobOptions);
                }
            }
        }
        builder.Services.AddPackConfigStore(packConfigBlobs);
        builder.Services.AddSingleton<DigitalBrain.Kernel.Abstractions.IOAuthStateProtector, DataProtectionOAuthStateProtector>();
        builder.Services.AddHostedService<DigitalBrain.Salesforce.SalesforceAppConfigSeeder>();
        builder.Services.AddHostedService<DigitalBrain.Google.GoogleAppConfigSeeder>();

        builder.Services.AddSingleton<DigitalBrain.Salesforce.ISalesforceApiClientFactory, DigitalBrain.Salesforce.SalesforceApiClientFactory>();
        builder.Services.AddSingleton<DigitalBrain.Google.IGmailApiClientFactory, DigitalBrain.Google.GmailApiClientFactory>();

        builder.Services.AddKeyedSingleton<DigitalBrain.Kernel.Abstractions.IConnector>("salesforce", (sp, _) => new DigitalBrain.Salesforce.SalesforceConnector(
            sp.GetRequiredService<DigitalBrain.Salesforce.ISalesforceApiClientFactory>(),
            sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>(),
            sp.GetRequiredService<DigitalBrain.Kernel.Abstractions.IOAuthStateProtector>()));
        builder.Services.AddKeyedSingleton<DigitalBrain.Kernel.Abstractions.IConnector>("google", (sp, _) => new DigitalBrain.Google.GoogleConnector(
            sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>(),
            sp.GetRequiredService<DigitalBrain.Kernel.Abstractions.IOAuthStateProtector>(),
            sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>()));

        builder.Services.AddHealthChecks()
            .AddAsyncCheck("google-connector", static _ => Task.FromResult(
                HealthCheckResult.Healthy("Google connector is registered")))
            .AddAsyncCheck("salesforce-connector", static _ => Task.FromResult(
                HealthCheckResult.Healthy("Salesforce connector is registered")));

        return builder;
    }

    public static WebApplication MapDigitalBrainSetup(this WebApplication app)
    {
        app.UseRouting();
        app.MapDefaultEndpoints();
        app.UseCors("browser");

        var webRoot = app.Configuration["DIGITALBRAIN_WEBROOT"];
        var serveWebBundle = !string.IsNullOrWhiteSpace(webRoot) && Directory.Exists(webRoot);
        if (serveWebBundle)
        {
            var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(webRoot!));
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        }

        if (serveWebBundle)
        {
            var indexPath = Path.Combine(Path.GetFullPath(webRoot!), "index.html");
            app.MapFallback(async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
            });
        }

        return app;
    }

    public static WebApplicationBuilder ConfigureDigitalBrainKestrel(this WebApplicationBuilder builder)
    {
        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted(builder.Configuration);

        builder.WebHost.ConfigureKestrel(options =>
        {
            if (isAspireHosted)
            {
                var webPort = Environment.GetEnvironmentVariable("DIGITALBRAIN_WEB_PORT");
                var hasWebEndpoint = int.TryParse(webPort, out var webEndpointPort);

                var grpcPorts = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var grpcPort in grpcPorts)
                {
                    if (int.TryParse(grpcPort, out var grpcEndpointPort) &&
                        (!hasWebEndpoint || grpcEndpointPort != webEndpointPort))
                    {
                        options.ListenAnyIP(grpcEndpointPort, listen => listen.Protocols = HttpProtocols.Http2);
                    }
                }

                if (hasWebEndpoint)
                {
                    options.ListenAnyIP(webEndpointPort, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
                }
                return;
            }

            options.ListenAnyIP(8080, listen => listen.Protocols = HttpProtocols.Http2);
            options.ListenAnyIP(8081, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
        });

        return builder;
    }
}
