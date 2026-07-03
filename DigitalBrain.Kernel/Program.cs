using Azure.Storage.Blobs;
using DigitalBrain.Core;
using DigitalBrain.Context;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Company;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Db;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.Market;
using DigitalBrain.Kernel.Uploads;
using DigitalBrain.Kernel.Ui;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.AI;
using Orleans.Configuration;
using Orleans.Journaling;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Economics;
using DigitalBrain.Kernel.Salesforce;
using DigitalBrain.Salesforce;
using NeuroOSPrototype.ServiceDefaults;

// Prototype silo host for DigitalBrain.
// Aspire-hosted path: env vars ConnectionStrings__clustering / grainstate / journal are injected by Aspire.
// Fast path (dotnet run --project DigitalBrain.Kernel): none of those env vars present → localhost clustering + in-memory journals.

#pragma warning disable ORLEANSEXP005

var builder = WebApplication.CreateBuilder(args);
var isAspireHosted = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__clustering"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__grainstate"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__journal"));

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    if (isAspireHosted)
    {
        var grpcPorts = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var grpcPort in grpcPorts)
        {
            if (int.TryParse(grpcPort, out var grpcEndpointPort))
            {
                options.ListenAnyIP(grpcEndpointPort, listen => listen.Protocols = HttpProtocols.Http2);
            }
        }

        var webPort = Environment.GetEnvironmentVariable("DIGITALBRAIN_WEB_PORT");
        if (int.TryParse(webPort, out var webEndpointPort))
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
    ?? new[] { "https://digitalbrain.tech" };

builder.Services.AddCors(options => options.AddPolicy("browser", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));

// Server-driven UI fanout: neurons broadcast RfwCards; WatchHomeFeed gRPC subscribers stream them.
// The per-silo HomeFeedStreamSubscriber (wired into the silo below) re-fans cards from the shared Orleans
// MemoryStream so cards broadcast on any silo reach all replicas.
builder.Services.AddSingleton<HomeFeedBus>();

// Signal egress fanout: neurons broadcast Signals on the timeline; WatchSynapses gRPC subscribers stream them
// filtered by type name. The per-silo SignalEgressStreamSubscriber (wired into the silo below) forwards Signals
// from the DigitalBrainTimeline stream to the SignalEgressBus. Like HomeFeed (proven in
// HomeFeedCrossSiloTests), Orleans MemoryStream explicit subscriptions deliver cluster-wide — every silo's
// subscriber receives every Signal regardless of which replica it was broadcast on.
builder.Services.AddSingleton<SignalEgressBus>();

// FileSystemNeuron delegates its System.IO logic to this ino-hosted, Orleans-free plain class.
builder.Services.AddSingleton<DigitalBrain.Windows.FileSystemOperations>();
builder.Services.AddSingleton<SqliteSchemaInspector>();

// RoslynNeuron delegates its MSBuildWorkspace analysis logic to this ino-hosted, Orleans-free plain class.
builder.Services.AddSingleton<DigitalBrain.Developer.RoslynAnalysisService>();
builder.Services.AddHttpClient<DigitalBrain.Kernel.Market.IMarketDataApiClient, DigitalBrain.Kernel.Market.CoinGeckoApiClient>();

// Co-host the MCP tool surface in-process. Only read-only tools are exposed over HTTP (remotely reachable);
// mutation tools are stdio-only (local/trusted) pending a remote auth decision.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrain.Mcp.DigitalBrainReadTools>();
builder.Services.AddSingleton<DigitalBrain.Mcp.DigitalBrainReadTools>();

if (isAspireHosted)
{
    // Cloud host (standalone ACA): bind the journal BlobServiceClient from ConnectionStrings__journal here;
    // clustering + grain storage are wired directly in UseOrleans below from their connection strings. (Under an
    // Aspire AppHost those would be wired by WithClustering/WithGrainStorage; in ACA the silo configures Orleans itself.)
    var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
    var grainStorageServiceKey = Environment.GetEnvironmentVariable("Orleans__GrainStorage__Default__ServiceKey") ?? "grainstate";

    builder.AddKeyedAzureTableServiceClient(clusteringServiceKey);
    builder.AddKeyedAzureBlobServiceClient(grainStorageServiceKey);

    // Non-keyed BlobServiceClient from grain storage for pack-config key ring persistence and blob backing.
    // Uses the same Azurite account as grain state but stores in a separate "pack-config" container.
    builder.AddAzureBlobServiceClient("grainstate");
}

builder.Services.AddDigitalBrainChat(builder.Configuration);
builder.Services.AddSingleton<DigitalBrain.Kernel.Llm.IScopedChatClientFactory, DigitalBrain.Kernel.Llm.ScopedChatClientFactory>();
builder.Services.AddKernelSecurity(builder.Configuration, builder.Environment);
builder.Services.AddEconomics(builder.Configuration);
builder.Services.AddContextStore(builder.Configuration);

// Aspire path: supply a BlobServiceClient so DataProtection keys are shared across all 3 HA replicas.
// Non-Aspire (local/test): no blobs → ephemeral key ring (single process only).
BlobServiceClient? packConfigBlobs = null;
if (isAspireHosted)
{
    var grainStateConnStr = builder.Configuration.GetConnectionString("grainstate");
    if (!string.IsNullOrEmpty(grainStateConnStr))
        packConfigBlobs = new BlobServiceClient(grainStateConnStr);
}
builder.Services.AddPackConfigStore(packConfigBlobs);
builder.Services.AddHostedService<SalesforceAppConfigSeeder>();
builder.Services.AddSingleton<ProcessCrystallizer>(sp => new ProcessCrystallizer(sp.GetService<IChatClient>()));
builder.Services.AddSingleton<SkillPackSynthesizer>();

// Google Gmail/Drive/Calendar API clients: one UserCredential per grain activation, built from the "google"/
// "default" pack config scope (client_id/client_secret/refresh_token), mirroring LlmResponderNeuron's per-scope
// IPackConfigStore resolution. Scoped (not singleton) because Orleans creates one DI scope per grain activation,
// so each GmailNeuron/GoogleDriveNeuron/GoogleCalendarNeuron activation resolves its own credential/service.
// GetAwaiter().GetResult() is safe here: grain activation runs on thread-pool threads with no captured
// SynchronizationContext, so there is no deadlock risk (the same reasoning ASP.NET Core middleware relies on).
builder.Services.AddScoped(sp => BuildGoogleCredential(sp, "google", "default"));
builder.Services.AddScoped<DigitalBrain.Google.IGmailApiClient>(sp =>
    new DigitalBrain.Google.GoogleGmailApiClient(sp.GetRequiredService<Google.Apis.Auth.OAuth2.UserCredential>()));
builder.Services.AddScoped<DigitalBrain.Google.IGoogleDriveApiClient>(sp =>
    new DigitalBrain.Google.GoogleDriveApiClient(sp.GetRequiredService<Google.Apis.Auth.OAuth2.UserCredential>()));
builder.Services.AddScoped<DigitalBrain.Google.IGoogleCalendarApiClient>(sp =>
    new DigitalBrain.Google.GoogleCalendarApiClient(sp.GetRequiredService<Google.Apis.Auth.OAuth2.UserCredential>()));

// Salesforce CRM REST API client: built from the encrypted "salesforce"/"default" pack config scope that
// the Salesforce credential prompt stores. Scoped for the same per-grain-activation reason as Google.
builder.Services.AddScoped<DigitalBrain.Salesforce.ISalesforceApiClient>(sp =>
    DigitalBrain.Salesforce.SalesforceClientFactory
        .CreateApiClientAsync(sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>())
        .GetAwaiter()
        .GetResult());

// Proxy to private marketplace (new separate repo) when enabled.
// Register the stub here; real impl uses HttpClient to the marketplace service.
var useRemote = builder.Configuration.GetValue("DigitalBrain:Marketplace:UseRemote", false);
if (useRemote)
{
    builder.Services.AddSingleton<IRemoteMarketplaceClient, DigitalBrain.Kernel.Marketplace.RemoteMarketplaceClientStub>();
}

builder.UseOrleans(siloBuilder =>
{
    siloBuilder.ConfigureServices(services => services.AddScoped<NeuronJournals>());

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
        // then the durable Blob journal. A stable cluster/service id lets the silo rejoin the same cluster on restart.
        var clusterId = builder.Configuration["Orleans:ClusterId"] ?? "digitalbrain";
        var serviceId = builder.Configuration["Orleans:ServiceId"] ?? "digitalbrain";

        siloBuilder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterId;
            options.ServiceId = serviceId;
        });
        siloBuilder.UseAzureStorageClustering(options =>
            options.ConfigureTableServiceClient(builder.Configuration.GetConnectionString("clustering")!));
        siloBuilder.AddAzureBlobGrainStorage("Default", options =>
            options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("grainstate")!));
        // Native ("orleans-binary") journal format, not UseJsonJournalFormat: a spike
        // (DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs) proved native round-trips every Synapse
        // subtype through a real grain deactivation/reactivation with zero manual type registration, while
        // Orleans.Journaling.Json (still preview/experimental) throws ResolverTypeInfoOptionsNotCompatible
        // the moment it's actually exercised against Azure Blob storage - the exact untested scenario the
        // spike's own caveats flagged as a risk. See DigitalBrain.Tests/Spikes/README.md for the full record.
        siloBuilder.AddAzureBlobJournalStorage(options =>
            options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("journal")!));
        siloBuilder.ConfigureServices(services =>
            services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = "orleans-binary"));
    }

    siloBuilder.AddMemoryStreams("HomeFeed");
    siloBuilder.AddMemoryStreams("DigitalBrainTimeline");
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    siloBuilder.ConfigureServices(services => services.AddHomeFeedStreamSubscriber());
    siloBuilder.ConfigureServices(services => services.AddSignalEgressStreamSubscriber());
    siloBuilder.AddFoundry();
});

#pragma warning restore ORLEANSEXP005

var app = builder.Build();

app.UseRouting();
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

    var sessionId = form["sessionId"].FirstOrDefault();
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

            var cmd = ChatUploadClassifier.BuildDbInspectSchema(file.FileName, tempPath, sessionId);
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
        sessionId));

    return Results.Ok();
});

app.MapGet(SalesforceClientFactory.DefaultCallbackPath, async (
    HttpRequest request,
    DigitalBrain.Core.Config.IPackConfigStore packConfigStore,
    IGrainFactory grains,
    ILogger<Program> callbackLogger) =>
{
    var returnedError = request.Query["error"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(returnedError))
    {
        var description = request.Query["error_description"].FirstOrDefault();
        return Results.Content(
            SalesforceCallbackPage("Salesforce login failed", $"{returnedError}: {description}".TrimEnd(':', ' ')),
            "text/html",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var code = request.Query["code"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.Content(
            SalesforceCallbackPage("Salesforce login failed", "The callback did not include an authorization code."),
            "text/html",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var values = await packConfigStore.GetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName);
    var returnedState = request.Query["state"].FirstOrDefault();
    if (values.TryGetValue(SalesforceClientFactory.OAuthStateKey, out var expectedState) &&
        !string.IsNullOrWhiteSpace(expectedState) &&
        !string.Equals(expectedState, returnedState, StringComparison.Ordinal))
    {
        return Results.Content(
            SalesforceCallbackPage("Salesforce login failed", "The callback state did not match the pending login."),
            "text/html",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var redirectUri = values.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var storedRedirectUri)
        ? storedRedirectUri
        : SalesforceCallbackUri(request);

    try
    {
        var tokenValues = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(values, code, redirectUri);
        var merged = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in tokenValues)
            merged[key] = value;
        merged.Remove(SalesforceClientFactory.OAuthStateKey);
        merged.Remove(SalesforceClientFactory.OAuthCodeVerifierKey);

        await packConfigStore.SetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName, merged);

        var ingress = grains.GetGrain<IIngressNeuron>("salesforce-auth-callback-" + Guid.NewGuid().ToString("N"));
        await ingress.IngestAsync("PackConfigured", new Dictionary<string, object?>
        {
            ["pack"] = SalesforceClientFactory.PackName,
            ["scope"] = SalesforceClientFactory.DefaultScope
        });
        await ingress.IngestAsync(SalesforceSignals.AuthCompleted, new Dictionary<string, object?>
        {
            ["provider"] = "salesforce",
            ["pack"] = SalesforceClientFactory.PackName,
            ["scope"] = SalesforceClientFactory.DefaultScope
        });

        return Results.Content(
            SalesforceCallbackPage("Salesforce connected", "You can close this browser tab and return to DigitalBrain."),
            "text/html");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        callbackLogger.LogWarning(ex, "Salesforce OAuth callback failed.");
        return Results.Content(
            SalesforceCallbackPage("Salesforce login failed", ex.GetBaseException().Message),
            "text/html",
            statusCode: StatusCodes.Status400BadRequest);
    }
});

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
var grainFactory = app.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var status = grainFactory.GetGrain<ISystemStatus>("status-main");
                await status.GetTimelineAsync();
                await grainFactory.GetGrain<IInoCodeEditor>("ino-editor-main").GetTimelineAsync();
                await grainFactory.GetGrain<IContextNeuron>("context-main").GetTimelineAsync();
                await grainFactory.GetGrain<IDbSupportNeuron>("db-main").GetTimelineAsync();
                await grainFactory.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();
                await grainFactory.GetGrain<IUserSessionNeuron>("session-main").GetTimelineAsync();

                // Activate the singleton LLM responder so it subscribes to the timeline at startup.
                // Broadcasts only reach already-activated grains; without this the AskLlm -> reply Signal
                // chain (e.g. the Telegram experience) would silently never fire in production. GetTimelineAsync
                // is idempotent — a no-op if the grain is already active.
                await grainFactory.GetGrain<ILlmResponderNeuron>(ILlmResponderNeuron.SingletonKey).GetTimelineAsync();

                // MarketDataNeuron has the same activate-before-broadcast requirement as ILlmResponderNeuron
                // above: it's an IHandle<Signal> grain that filters Signal("CheckBitcoinPrice") off the
                // timeline, so it must be activated before that broadcast arrives or it never fires.
                await grainFactory.GetGrain<IMarketDataNeuron>("market-data-main").GetTimelineAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Kernel startup neuron warmup failed.");
            }
        });
    });
}

app.Run();

// Reads client_id/client_secret/refresh_token from the given pack-config scope/pack and builds a UserCredential.
// Config not yet provided (first run, before "Sign in with Google" completes) throws so grain activation fails
// fast and loudly rather than silently constructing a service that will 401 on first real call — mirrors
// LlmResponderNeuron's fallback-to-null shape being unavailable here since UserCredential is non-nullable.
static Google.Apis.Auth.OAuth2.UserCredential BuildGoogleCredential(IServiceProvider sp, string pack, string scope)
{
    var store = sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>();
    var values = store.GetAsync(scope, pack).GetAwaiter().GetResult();

    if (!values.TryGetValue("client_id", out var clientId) ||
        !values.TryGetValue("client_secret", out var clientSecret) ||
        !values.TryGetValue("refresh_token", out var refreshToken))
    {
        throw new InvalidOperationException(
            $"Google pack config (scope '{scope}', pack '{pack}') is missing client_id/client_secret/refresh_token. " +
            "Complete \"Sign in with Google\" before using Gmail/Drive/Calendar neurons.");
    }

    return DigitalBrain.Google.GoogleCredentialFactory.FromRefreshToken(
        clientId, clientSecret, refreshToken,
        Google.Apis.Gmail.v1.GmailService.ScopeConstants.MailGoogleCom,
        Google.Apis.Drive.v3.DriveService.ScopeConstants.Drive,
        Google.Apis.Calendar.v3.CalendarService.ScopeConstants.Calendar);
}

static string SalesforceCallbackUri(HttpRequest request) =>
    new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? -1, SalesforceClientFactory.DefaultCallbackPath)
        .Uri
        .ToString();

static string SalesforceCallbackPage(string title, string message)
{
    var safeTitle = System.Net.WebUtility.HtmlEncode(title);
    var safeMessage = System.Net.WebUtility.HtmlEncode(message);
    return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>{{safeTitle}}</title>
          <style>
            body { font-family: system-ui, sans-serif; margin: 3rem; line-height: 1.5; }
            main { max-width: 42rem; }
          </style>
        </head>
        <body>
          <main>
            <h1>{{safeTitle}}</h1>
            <p>{{safeMessage}}</p>
          </main>
        </body>
        </html>
        """;
}

public partial class Program;

