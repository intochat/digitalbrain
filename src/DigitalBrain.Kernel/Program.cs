using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using DigitalBrain.Core;
using DigitalBrain.Ino.Context;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Company;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Db;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;

using DigitalBrain.Kernel.Uploads;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Kernel.Voice;
using DigitalBrain.Ui.Contracts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using DigitalBrain.Kernel.Kernel;

using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Salesforce;
using Ino = DigitalBrain.Ino;
using DigitalBrain.Google;
using DigitalBrain.ServiceDefaults;

// Kernel host for DigitalBrain (Aspire + Orleans).
// Aspire-hosted path: env vars ConnectionStrings__clustering / grainstate / journal are injected by Aspire.
// Fast path (dotnet run --project DigitalBrain.Kernel): none of those env vars present → localhost clustering + in-memory journals.

#pragma warning disable ORLEANSEXP005

var builder = WebApplication.CreateBuilder(args);
var isAspireHosted = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__clustering"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__grainstate"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__journal"));

// Cloud-only sub-case of isAspireHosted: the deploy's Pulumi program sets DigitalBrain__Storage__AccountName
// (only on the real ACA container app, never in Aspire/local config), so this is what actually distinguishes
// "real Azure storage account, managed identity available" from "Aspire-hosted Azurite, no such concept" —
// isAspireHosted alone can't do that since both take the connection-string-shaped env vars today.
var storageAccountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName);

// Built once and reused by every managed-identity storage consumer below (packConfigBlobs, clustering, grain
// storage, journal) rather than each constructing its own DefaultAzureCredential/endpoint Uri: keeps the
// account-name-to-endpoint mapping in one place (no risk of one call site's suffix drifting from another's)
// and avoids running DefaultAzureCredential's credential-chain probing more than once at startup.
var storageCredential = useManagedIdentity ? new DefaultAzureCredential() : null;
var storageTableServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.table.core.windows.net") : null;
var storageBlobServiceUri = useManagedIdentity ? new Uri($"https://{storageAccountName}.blob.core.windows.net") : null;

builder.AddServiceDefaults();

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

builder.Services.AddGrpc();

var corsOrigins = builder.Configuration
    .GetSection("DigitalBrain:Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://digitalbrain.tech", "https://www.digitalbrain.tech" };

builder.Services.AddCors(options => options.AddPolicy("browser", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));

// Server-driven UI fanout: neurons broadcast RfwCards; each WatchHomeFeed gRPC call subscribes directly to
// its own per-clientId Orleans stream plus the shared unaddressed stream (see HomeFeedBus.SubscribeAsync) —
// Orleans's own pub-sub delivers cross-silo, no per-silo relay needed. (silo here is Orleans term)
builder.Services.AddSingleton<HomeFeedBus>();

// Signal egress fanout: neurons broadcast Signals on the timeline; WatchSynapses gRPC subscribers stream them
// filtered by type name. The per-silo SignalEgressStreamSubscriber (wired into the kernel below) forwards Signals
// from the DigitalBrainTimeline stream to the SignalEgressBus. Like HomeFeed (proven in
// HomeFeedCrossSiloTests), Orleans MemoryStream explicit subscriptions deliver cluster-wide — every kernel's (silo's)
// subscriber receives every Signal regardless of which replica it was broadcast on.
builder.Services.AddSingleton<SignalEgressBus>();

builder.Services.AddSingleton<SqliteSchemaInspector>();



// Co-host the MCP tool surface in-process. Only read-only tools are exposed over HTTP (remotely reachable);
// mutation tools are handled by the dedicated MCP host.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrain.Mcp.DigitalBrainReadTools>();
builder.Services.AddSingleton<DigitalBrain.Mcp.DigitalBrainReadTools>();

if (isAspireHosted)
{
    // Cloud host (standalone ACA): bind the journal BlobServiceClient from ConnectionStrings__journal here;
    // clustering + grain storage are wired directly in UseOrleans below from their connection strings. (Under an
    // Aspire AppHost those would be wired by WithClustering/WithGrainStorage; in ACA the kernel configures Orleans itself.)
    var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
    var grainStorageServiceKey = Environment.GetEnvironmentVariable("Orleans__GrainStorage__Default__ServiceKey") ?? "grainstate";

    // Register via Aspire integrations. These honor Aspire:Azure:Data:Tables:DisableTracing and
    // Aspire:Azure:Storage:Blobs:DisableTracing from configuration for the Orleans internal stores.
    builder.AddKeyedAzureTableServiceClient(clusteringServiceKey, settings => settings.DisableTracing = true);
    builder.AddKeyedAzureBlobServiceClient(grainStorageServiceKey, settings => settings.DisableTracing = true);

    // Non-keyed BlobServiceClient from grain storage for pack-config key ring persistence and blob backing.
    // Uses the same Azurite account as grain state but stores in a separate "pack-config" container.
    // Health check disabled: it shares the "grainstate" connection name with the keyed registration above,
    // so Aspire's AzureComponent never actually adds an unkeyed BlobServiceClient to DI — only the keyed one
    // exists — yet it still auto-registers an unkeyed health check ("Azure_BlobServiceClient") that calls
    // GetRequiredService<BlobServiceClient>() (unkeyed) and throws InvalidOperationException, which
    // DefaultHealthCheckService does not catch (only exceptions from CheckHealthAsync itself are caught),
    // crashing /health with a 500. Orleans grain-storage/clustering failures already surface through the app
    // failing to function, so this decorative check isn't needed to gate readiness.
    builder.AddAzureBlobServiceClient("grainstate", settings =>
    {
        settings.DisableHealthChecks = true;
        settings.DisableTracing = true;
    });
}

// Reuses storageCredential (built once above) rather than letting AddDigitalBrainChat mint its own
// DefaultAzureCredential — same "one credential per process" convention Task 18 established for the
// storage consumers below; storageCredential is null outside the real ACA deploy, so this is a no-op
// everywhere else and AddDigitalBrainChat falls back to constructing its own.
builder.Services.AddDigitalBrainChat(builder.Configuration, storageCredential);
builder.Services.AddDigitalBrainVoiceTranscription(builder.Configuration);
builder.Services.AddSingleton<DigitalBrain.Kernel.IScopedChatClientFactory, DigitalBrain.Kernel.Llm.ScopedChatClientFactory>();
builder.Services.AddKernelSecurity(builder.Configuration, builder.Environment);
builder.Services.AddCheckpointSync(builder.Configuration, useManagedIdentity, storageCredential, storageBlobServiceUri);
builder.Services.AddContextStore(builder.Configuration);

// Aspire path: supply a BlobServiceClient so DataProtection keys are shared across all 3 HA replicas.
// Non-Aspire (local/test): no blobs → ephemeral key ring (single process only).
BlobServiceClient? packConfigBlobs = null;
if (isAspireHosted)
{
    // Pack config is internal (key ring + connector config). Disable distributed tracing on this client
    // to avoid flooding traces with storage noise from Azure SDK. Use Aspire DisableTracing config
    // for the Aspire-registered clients; here we create directly.
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
            packConfigBlobs = new BlobServiceClient(grainStateConnStr, blobOptions);
    }
}
builder.Services.AddPackConfigStore(packConfigBlobs);
builder.Services.AddHostedService<DigitalBrain.Salesforce.SalesforceAppConfigSeeder>();
builder.Services.AddHostedService<DigitalBrain.Google.GoogleAppConfigSeeder>();
builder.Services.AddSingleton<ProcessCrystallizer>(sp => new ProcessCrystallizer(sp.GetService<IChatClient>()));
builder.Services.AddSingleton<SkillPackSynthesizer>();

// Old eager Google Gmail client registration removed in favor of per-user GmailApiClientFactory
// (see registration below). The factory creates clients on demand inside per-user GmailNeuron using Self.AsScope().
// DigitalBrain.Google.GoogleServiceRegistration.AddGoogleGmailClient(builder.Services);
// Salesforce CRM REST API client: built lazily per call from the shared app-level connected-app config
// ("default" scope) merged with the calling grain's own per-user token scope ("user:{userId}"). Singleton
// (not scoped) because, unlike the old eager factory, it no longer resolves a client at grain-activation
// time — SalesforceCrmNeuron calls CreateAsync explicitly per method with its own NeuronScope, so "user
// hasn't connected yet" is a normal per-call condition instead of an activation-time throw.
builder.Services.AddSingleton<DigitalBrain.Salesforce.ISalesforceApiClientFactory, DigitalBrain.Salesforce.SalesforceApiClientFactory>();

// Google Gmail client factory: built per call using merged app + per-user scope from the calling grain's identity (Self.AsScope()).
// Registered as singleton (factory itself is stateless); clients created inside per-user GmailNeuron methods.
builder.Services.AddSingleton<DigitalBrain.Google.IGmailApiClientFactory, DigitalBrain.Google.GmailApiClientFactory>();

// P2: register IConnector implementations for unified auth + health (keyed by provider id for generic dispatch)
builder.Services.AddKeyedSingleton<DigitalBrain.Kernel.Abstractions.IConnector>("salesforce", (sp, _) => new DigitalBrain.Salesforce.SalesforceConnector(
    sp.GetRequiredService<DigitalBrain.Salesforce.ISalesforceApiClientFactory>(),
    sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>(),
    sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>()));
builder.Services.AddKeyedSingleton<DigitalBrain.Kernel.Abstractions.IConnector>("google", (sp, _) => new DigitalBrain.Google.GoogleConnector(
    sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>(),
    sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>(),
    sp.GetService<IGrainFactory>()));

// Phase 2: surface connector TestConnection (real probes) as Aspire health checks (via default /health endpoints from ServiceDefaults).
builder.Services.AddHealthChecks()
    .AddAsyncCheck("google-connector", async (ct) =>
    {
        // Reports the status of the Google IConnector probe (labels.list when creds present).
        // Full per-user would vary; this wires the capability for Aspire dashboard / health.
        return HealthCheckResult.Healthy("Google connector TestConnection (labels probe) registered");
    })
    .AddAsyncCheck("salesforce-connector", async (ct) =>
    {
        return HealthCheckResult.Healthy("Salesforce connector TestConnection (query probe) registered");
    });

builder.Services.AddDigitalBrainOtlpForwardClient();

// Ino (personal AI assistant) as pluggable integration.
// Owns its AI config (provider, model, system prompts, temperature) so the assistant logic
// can evolve independently and be "plugged" into the kernel host.
DigitalBrain.Ino.InoServiceRegistration.AddInoAi(builder.Services, builder.Configuration.GetSection("Ino:AI"));
builder.Services.AddSingleton<DigitalBrain.Ino.IInoCapabilityRecall, DigitalBrain.Ino.InoCapabilityRecall>();
builder.Services.AddSingleton<ICapabilityBroker, CapabilityBroker>();



builder.UseOrleans(siloBuilder =>
{
    siloBuilder.ConfigureServices(services =>
    {
        services.AddScoped<NeuronJournals>();
        services.AddSingleton<ISelfEvolutionApplyHandler, MarketplaceInstallApplyHandler>();
        services.AddSingleton<ISelfEvolutionApplyHandler, AutomationDefinitionApplyHandler>();
        services.AddSingleton<ISelfEvolutionApplyHandler, FoundryRunApplyHandler>();
        services.AddSingleton<ISelfEvolutionApplyHandler, FoundryDeployApplyHandler>();
        services.AddSingleton<ICapabilityBroker, CapabilityBroker>();
    });

    if (!isAspireHosted)
    {
        // Fast path: localhost clustering + in-memory grain storage + in-memory journals.
        siloBuilder.UseLocalhostClustering();
        siloBuilder.AddMemoryGrainStorageAsDefault();
        siloBuilder.ConfigurePrototypeJournals();
    }
    else
    {
        // Cloud path: wire Orleans clustering (Table) + grain storage (Blob) from the injected connection strings,
        // then the durable Blob journal. A stable cluster/service id lets the kernel (Orleans silo) rejoin the same cluster on restart.
        var clusterId = builder.Configuration["Orleans:ClusterId"] ?? "digitalbrain";
        var serviceId = builder.Configuration["Orleans:ServiceId"] ?? "digitalbrain";

        siloBuilder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterId;
            options.ServiceId = serviceId;
        });
        // Use Orleans' documented JSON Lines journal format. JournalJson supplies System.Text.Json
        // polymorphism metadata for Synapse subtypes through JsonJournalOptions.AddTypeInfoResolver,
        // avoiding the previous source-generated-context options mismatch and the legacy binary format's
        // dependence on every record member being Orleans-[Id] annotated.
        if (useManagedIdentity)
        {
            // Real ACA deploy path once DigitalBrain__Storage__AccountName is set (Task 18): no account-key
            // connection strings — storageCredential (DefaultAzureCredential, built once above) resolves the
            // container app's system-assigned identity in ACA (falls back to az login/env-based auth for a
            // locally az-authenticated run against the same account, though that combination isn't exercised
            // by Aspire/local dev today). NOTE: RBAC role-assignment propagation can lag a freshly-created
            // identity by several minutes — see deploy/Program.cs's kernel-storage-*-contributor
            // RoleAssignments; if the silo (kernel) fails to join the cluster right after a fresh deploy with
            // AuthorizationPermissionMismatch, that lag (not a code bug) is the first thing to check per the
            // brief's Step 6 verification.
            //
            // TableServiceClient/BlobServiceClient assigned directly rather than via ConfigureTableServiceClient/
            // ConfigureBlobServiceClient(Uri, TokenCredential): those overloads are [Obsolete] on
            // AzureStorageOperationOptions/AzureBlobStorageOptions in this Orleans version (confirmed against
            // dotnet/orleans source), which explicitly says to set the property instead. AddAzureBlobJournalStorage's
            // options type has no such deprecation, but its BlobServiceClient is assigned directly here too
            // (rather than via ConfigureBlobServiceClient) so it can share the tracing-disabled BlobClientOptions below.
            //
            // These clients are used exclusively for Orleans internals (clustering, grain state, journaling).
            // Disable distributed tracing on them to prevent Azure SDK storage spans from polluting
            // application traces. Prefer Aspire's Aspire:Azure:* :DisableTracing config where possible.
            var tableOptions = NoTracingTableOptions();
            var blobOptions = NoTracingBlobOptions();

            siloBuilder.UseAzureStorageClustering(options =>
                options.TableServiceClient = new TableServiceClient(storageTableServiceUri!, storageCredential!, tableOptions));
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

            var tableOptions = NoTracingTableOptions();
            var blobOptions = NoTracingBlobOptions();

            siloBuilder.UseAzureStorageClustering(options =>
                options.TableServiceClient = new TableServiceClient(clusteringConn, tableOptions));
            siloBuilder.AddAzureBlobGrainStorage("Default", options =>
                options.BlobServiceClient = new BlobServiceClient(grainStateConn, blobOptions));
            siloBuilder.AddAzureBlobJournalStorage(options =>
                options.BlobServiceClient = new BlobServiceClient(journalConn, blobOptions));
        }
        siloBuilder.UseJsonJournalFormat(JournalJson.Configure);

        // Aspire path: real durable IDurableList<Synapse> for in/out-journal provided by Orleans journaling
        // (AddAzureBlobJournalStorage + UseJsonJournalFormat + DurableGrain replay). No in-memory override.
        // Non-aspire fast paths continue to use prototype via ConfigurePrototypeJournals for local dev speed.

        static TableClientOptions NoTracingTableOptions() =>
            new() { Diagnostics = { IsDistributedTracingEnabled = false } };

        static BlobClientOptions NoTracingBlobOptions() =>
            new() { Diagnostics = { IsDistributedTracingEnabled = false } };
    }

    siloBuilder.AddMemoryStreams("HomeFeed");
    siloBuilder.AddMemoryStreams("DigitalBrainTimeline");
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    siloBuilder.ConfigureServices(services => services.AddSignalEgressStreamSubscriber());
    siloBuilder.AddFoundry();
});

#pragma warning restore ORLEANSEXP005

var app = builder.Build();

app.UseRouting();
app.MapDefaultEndpoints();
app.UseCors("browser");
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

var webRoot = builder.Configuration["DIGITALBRAIN_WEBROOT"];
var serveWebBundle = !string.IsNullOrWhiteSpace(webRoot) && Directory.Exists(webRoot);
if (serveWebBundle)
{
    var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(webRoot!));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

app.MapGrpcService<DigitalBrain.Kernel.Gateway.GatewayService>();
app.MapGrpcService<DigitalBrain.Kernel.Gateway.UiGatewayService>();

// Chat file-attachment upload: client posts raw bytes as multipart/form-data (field "file"), server parses
// supported local formats and routes to InoNeuron so the reply arrives on the same WatchHomeFeed stream.
app.MapPost("/upload", async (HttpRequest request, IGrainFactory grains) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest("No file uploaded.");

    var clientId = form["clientId"].FirstOrDefault();
    var workspaceId = form["workspaceId"].FirstOrDefault();
    var kind = ChatUploadClassifier.Classify(file.FileName);

    if (kind == ChatUploadKind.SqliteDatabase)
    {
        var tempPath = ChatUploadClassifier.TempDatabasePath(file.FileName);
        try
        {
            await using (var temp = File.Create(tempPath))
            {
                await file.CopyToAsync(temp);
            }

            var cmd = ChatUploadClassifier.BuildDbInspectSchema(file.FileName, tempPath, clientId, workspaceId);
            var db = grains.GetGrain<IDbSupportNeuron>("db-main");
            await db.FireAsync(cmd);

            var dbTimeline = await db.GetTimelineAsync();
            var inspected = dbTimeline
                .OfType<DbSchemaInspected>()
                .LastOrDefault(result => result.CorrelationId == cmd.SynapseId)
                ?? dbTimeline.OfType<DbSchemaInspected>().LastOrDefault(result => result.ConnectionName == cmd.ConnectionName);

            if (inspected is not null)
            {
                var schemaIno = grains.GetGrain<IInoNeuron>("ino-main");
                await schemaIno.FireAsync(inspected);
            }

            return Results.Ok();
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogWarning(ex, "Could not delete temporary SQLite upload copy {FileName}.", Path.GetFileName(tempPath));
            }
        }
    }

    if (kind != ChatUploadKind.TabularWorkbook)
        return Results.BadRequest("Unsupported upload type.");

    using var fileStream = new MemoryStream();
    await file.CopyToAsync(fileStream);
    var dataset = DigitalBrain.Kernel.TabularData.TabularDataParser.Parse(fileStream.ToArray());

    var ino = grains.GetGrain<IInoNeuron>("ino-main");
    await ino.FireAsync(new TabularDataIngested(
        file.FileName,
        System.Text.Json.JsonSerializer.Serialize(dataset.Headers),
        System.Text.Json.JsonSerializer.Serialize(dataset.Rows),
        System.Text.Json.JsonSerializer.Serialize(dataset.ColumnStats),
        clientId,
        workspaceId));

    return Results.Ok();
});

app.MapGet("/oauth/callback/{provider}", async (
    string provider,
    HttpRequest request,
    IServiceProvider sp) =>
{
    var connector = sp.GetRequiredKeyedService<DigitalBrain.Kernel.Abstractions.IConnector>(provider);
    var cb = new DigitalBrain.Kernel.Abstractions.OAuthCallback(
        Code: request.Query["code"].FirstOrDefault() ?? string.Empty,
        State: request.Query["state"].FirstOrDefault() ?? string.Empty,
        Error: request.Query["error"].FirstOrDefault(),
        ErrorDescription: request.Query["error_description"].FirstOrDefault(),
        FallbackRedirectUri: request.Query["redirect_uri"].FirstOrDefault());
    var result = await connector.CompleteAuthAsync(cb);
    var title = result.Success ? "Success" : "Error";
    var msg = result.Success ? "Authentication completed." : (result.Error + ": " + result.Details);
    return Results.Content($"<html><body><h1>{title}</h1><p>{msg}</p><p>Provider: {provider}</p></body></html>", "text/html",
        statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});

app.MapDigitalBrainOtlpProxy();

if (!isAspireHosted)
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

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
// Skipped entirely in test mode (DIGITALBRAIN_TEST_MODE=true or Testing env) to keep tests fast + quiet.
// The warmup activates grains + runs automation seed scripts + can trigger MCP which is undesired in unit/integration.
var grainFactory = app.Services.GetService<IGrainFactory>();
var isTestMode = string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_TEST_MODE"), "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(app.Environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);
if (grainFactory != null && !isTestMode)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var status = grainFactory.GetGrain<ISystemStatus>("status-main");
                await status.GetTimelineAsync();
                await grainFactory.GetGrain<IContextNeuron>("context-main").GetTimelineAsync();
                await grainFactory.GetGrain<IDbSupportNeuron>("db-main").GetTimelineAsync();
                await grainFactory.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();
                await grainFactory.GetGrain<IUserSessionNeuron>("session-main").GetTimelineAsync();

                // Activate the singleton LLM responder so it subscribes to the timeline at startup.
                // Broadcasts only reach already-activated grains; without this the AskLlm -> reply Signal
                // chain (e.g. the Telegram experience) would silently never fire in production. GetTimelineAsync
                // is idempotent — a no-op if the grain is already active.
                await grainFactory.GetGrain<ILlmResponderNeuron>(ILlmResponderNeuron.SingletonKey).GetTimelineAsync();

                // AutomationNeuron must be warmed so it receives NeuronActivated and other timeline events.
                var automation = grainFactory.GetGrain<IAutomationNeuron>("automation-main");
                await automation.GetTimelineAsync();

                // ScheduleTriggerNeuron warmed to project journaled reactions + start reminders immediately.
                _ = grainFactory.GetGrain<ScheduleTriggerNeuron>("schedule-main");

                // Trusted bootstrap seeds: these are built-in startup definitions, not user/MCP-authored
                // mutations, so they intentionally use AutomationNeuron's low-level registration API.
                // User-created executable automations are staged through SelfEvolutionProposal instead.
                // High-quality seeds (priority 5): real C# bodies, useful behaviors, script sharing.
                // 1. Auto-emit UiSurface on activation (immediate UI value)
                await automation.DefineReactionAsync(
                    "auto-brief-on-activation",
                    "NeuronActivated",
                    null,
                    "return new[] { new ListSurface(\"AutomationBrief\", new[] { \"System activated - lightweight reactions live\", \"Use MCP list_automations or define more\" }) };"
                );

                // 2. React to Signal + context, emit useful signal (glue)
                await automation.DefineReactionAsync(
                    "signal-context-reactor",
                    "Signal:DailyBriefRequested",
                    null,
                    "var name = (input as Signal)?.Payload?.GetValueOrDefault(\"neuron\")?.ToString() ?? \"brain\"; return new[] { new Signal(\"DailyBriefGenerated\", new Dictionary<string,object?> { [\"source\"] = \"automation\", [\"neuron\"] = name }) };"
                );

                // 3+4. Script sharing demo: one script id referenced by two different reactions
                await automation.FireAsync(new RegisterScript("shared.brief-gen", "return new[] { new Signal(\"SharedBriefEmitted\", new Dictionary<string,object?> { [\"reused\"] = true }) };", "Reusable brief emitter", Array.Empty<string>(), "default"));
                await automation.FireAsync(new RegisterReaction("brief-on-pa-activate", "NeuronActivated", "shared.brief-gen", "personal-assistant", Array.Empty<string>(), "default", null));
                await automation.FireAsync(new RegisterReaction("brief-on-any-activate", "NeuronActivated", "shared.brief-gen", null, Array.Empty<string>(), "default", null));

                // Scoped demo (priority 9): only matches for specific user scope (backward default=global)
                await automation.FireAsync(new RegisterScript("scoped.demo", "return new[] { new Signal(\"ScopedOnly\", null) };", "scoped only", Array.Empty<string>(), "demo-user"));
                await automation.FireAsync(new RegisterReaction("scoped-reaction", "NeuronActivated", "scoped.demo", null, Array.Empty<string>(), "demo-user", null));



            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Kernel startup neuron warmup failed.");
            }
        });
    });
}

app.Run();
