using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using DigitalBrain.Ino.Context;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Db;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Kernel.Voice;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace DigitalBrain.Kernel.Hosting;

public static class DigitalBrainOrleansExtensions
{
    public static IHostApplicationBuilder UseDigitalBrainOrleans(this IHostApplicationBuilder builder)
    {
        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted();

        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);

        var storageCredential = useManagedIdentity ? new DefaultAzureCredential() : null;
        var storageTableServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.table.core.windows.net") : null;
        var storageBlobServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.blob.core.windows.net") : null;

        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder.ConfigureServices(services =>
            {
                services.AddScoped<NeuronJournals>();
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationDefinitionApplyHandler>();
                services.AddSingleton<ICapabilityBroker, CapabilityBroker>();
            });

            if (!isAspireHosted)
            {
                siloBuilder.UseLocalhostClustering();
                siloBuilder.UseInMemoryReminderService();
                siloBuilder.AddMemoryGrainStorageAsDefault();
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
                    var blobOptions = new BlobClientOptions { Diagnostics = { IsDistributedTracingEnabled = false } };

                    siloBuilder.UseAzureStorageClustering(options =>
                        options.TableServiceClient = new TableServiceClient(storageTableServiceUri!, storageCredential!, tableOptions));
                    siloBuilder.UseAzureTableReminderService(options =>
                    {
                        options.TableServiceClient = new TableServiceClient(storageTableServiceUri!, storageCredential!, tableOptions);
                        options.TableName = "OrleansReminders";
                    });
                    siloBuilder.AddAzureBlobGrainStorage("Default", options =>
                        options.BlobServiceClient = new BlobServiceClient(storageBlobServiceUri!, storageCredential!, blobOptions));
                    siloBuilder.AddAzureBlobJournalStorage(options =>
                        options.BlobServiceClient = new BlobServiceClient(storageBlobServiceUri!, storageCredential!, blobOptions));
                }
                else
                {
                    var clusteringConn = builder.Configuration.GetConnectionString("clustering")!;
                    var grainStateConn = builder.Configuration.GetConnectionString("grainstate")!;
                    var journalConn = builder.Configuration.GetConnectionString("journal")!;

                    var tableOptions = new TableClientOptions { Diagnostics = { IsDistributedTracingEnabled = false } };
                    var blobOptions = new BlobClientOptions { Diagnostics = { IsDistributedTracingEnabled = false } };

                    siloBuilder.UseAzureStorageClustering(options =>
                        options.TableServiceClient = new TableServiceClient(clusteringConn, tableOptions));
                    siloBuilder.UseAzureTableReminderService(options =>
                    {
                        options.TableServiceClient = new TableServiceClient(clusteringConn, tableOptions);
                        options.TableName = "OrleansReminders";
                    });
                    siloBuilder.AddAzureBlobGrainStorage("Default", options =>
                        options.BlobServiceClient = new BlobServiceClient(grainStateConn, blobOptions));
                    siloBuilder.AddAzureBlobJournalStorage(options =>
                        options.BlobServiceClient = new BlobServiceClient(journalConn, blobOptions));
                }

                siloBuilder.UseJsonJournalFormat(JournalJson.Configure);
            }

            siloBuilder.AddMemoryStreams("HomeFeed");
            siloBuilder.AddMemoryStreams(SynapseStream.ProviderName);
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.ConfigureServices(services => services.AddSignalEgressStreamSubscriber());
        });

        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainClients(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<HomeFeedBus>();
        builder.Services.AddSingleton<SignalEgressBus>();
        builder.Services.AddSingleton<SqliteSchemaInspector>();

        builder.Services.AddGrpc();

        var corsOrigins = builder.Configuration
            .GetSection("DigitalBrain:Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "https://digitalbrain.tech", "https://www.digitalbrain.tech" };

        builder.Services.AddCors(options => options.AddPolicy("browser", policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<DigitalBrain.Mcp.DigitalBrainReadTools>();
        builder.Services.AddSingleton<DigitalBrain.Mcp.DigitalBrainReadTools>();

        builder.Services.AddHostedService<KernelStartupWarmupService>();

        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted();

        var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);

        var storageCredential = useManagedIdentity ? new DefaultAzureCredential() : null;
        var storageBlobServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.blob.core.windows.net") : null;

        if (isAspireHosted)
        {
            var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
            var grainStorageServiceKey = Environment.GetEnvironmentVariable("Orleans__GrainStorage__Default__ServiceKey") ?? "grainstate";

            builder.AddKeyedAzureTableServiceClient(clusteringServiceKey, settings => settings.DisableTracing = true);
            builder.AddKeyedAzureBlobServiceClient(grainStorageServiceKey, settings => settings.DisableTracing = true);

            builder.AddAzureBlobServiceClient("grainstate", settings =>
            {
                settings.DisableHealthChecks = true;
                settings.DisableTracing = true;
            });
        }

        builder.Services.AddDigitalBrainChat(builder.Configuration, storageCredential);
        builder.Services.AddDigitalBrainVoiceTranscription(builder.Configuration);
        builder.Services.AddSingleton<DigitalBrain.Kernel.IScopedChatClientFactory, DigitalBrain.Kernel.Llm.ScopedChatClientFactory>();
        builder.Services.AddKernelSecurity(builder.Configuration, builder.Environment);
        builder.Services.AddCheckpointSync(builder.Configuration, useManagedIdentity, storageCredential, storageBlobServiceUri);
        builder.Services.AddContextStore(builder.Configuration);

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
        builder.Services.AddHostedService<DigitalBrain.Salesforce.SalesforceAppConfigSeeder>();
        builder.Services.AddHostedService<DigitalBrain.Google.GoogleAppConfigSeeder>();

        builder.Services.AddSingleton<DigitalBrain.Salesforce.ISalesforceApiClientFactory, DigitalBrain.Salesforce.SalesforceApiClientFactory>();
        builder.Services.AddSingleton<DigitalBrain.Google.IGmailApiClientFactory, DigitalBrain.Google.GmailApiClientFactory>();

        builder.Services.AddKeyedSingleton<DigitalBrain.Kernel.Abstractions.IConnector>("salesforce", (sp, _) => new DigitalBrain.Salesforce.SalesforceConnector(
            sp.GetRequiredService<DigitalBrain.Salesforce.ISalesforceApiClientFactory>(),
            sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>(),
            sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>()));
        builder.Services.AddKeyedSingleton<DigitalBrain.Kernel.Abstractions.IConnector>("google", (sp, _) => new DigitalBrain.Google.GoogleConnector(
            sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>(),
            sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),
            sp.GetService<IGrainFactory>()));

        builder.Services.AddHealthChecks()
            .AddAsyncCheck("google-connector", async (ct) =>
            {
                return HealthCheckResult.Healthy("Google connector TestConnection (labels probe) registered");
            })
            .AddAsyncCheck("salesforce-connector", async (ct) =>
            {
                return HealthCheckResult.Healthy("Salesforce connector TestConnection (query probe) registered");
            });

        builder.Services.AddDigitalBrainOtlpForwardClient();

        DigitalBrain.Ino.InoServiceRegistration.AddInoAi(builder.Services, builder.Configuration.GetSection("Ino:AI"));
        builder.Services.AddSingleton<DigitalBrain.Ino.IInoCapabilityRecall, DigitalBrain.Ino.InoCapabilityRecall>();
        builder.Services.AddSingleton<ICapabilityBroker, CapabilityBroker>();

        return builder;
    }

    public static WebApplication MapDigitalBrainSetup(this WebApplication app)
    {
        app.UseRouting();
        app.MapDefaultEndpoints();
        app.UseCors("browser");
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

        var webRoot = app.Configuration["DIGITALBRAIN_WEBROOT"];
        var serveWebBundle = !string.IsNullOrWhiteSpace(webRoot) && Directory.Exists(webRoot);
        if (serveWebBundle)
        {
            var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(webRoot!));
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        }

        app.MapGrpcService<DigitalBrain.Kernel.Gateway.GatewayService>();
        app.MapGrpcService<DigitalBrain.Kernel.Gateway.UiGatewayService>();

        app.MapDigitalBrainOtlpProxy();

        if (!DigitalBrainHostEnvironment.IsAspireHosted())
        {
            app.MapMcp().RequireHost("*:8081");
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
        var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted();

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
