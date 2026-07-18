using System.Text.Json;
using Ino.ServiceDefaults;
using Ino.Telegram.Host;
using Ino.Telegram.Host.Services;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));

// Single bot client. Token is allowed to be empty (booting without a token is
// the demo-friendly path — see TelegramBotOptions for rationale); the underlying
// SDK will throw on the first API call instead, which is what the rest of the
// pipeline guards against by checking BotToken before scheduling those calls.
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IOptions<TelegramBotOptions>>().Value;
    return new TelegramBotClient(string.IsNullOrWhiteSpace(config.BotToken) ? "0:DISABLED" : config.BotToken);
});

builder.Services.AddHttpClient();

// Voice-to-text — Foundry Local Whisper. Hosted service so the model
// download + load runs at silo startup; voice messages sent before init
// completes get a clear "still initializing" reply instead of a hang.
builder.Services.AddSingleton<IAudioConverter, AudioConverter>();
builder.Services.AddSingleton<FoundryLocalTranscriptionService>();
builder.Services.AddSingleton<IAudioTranscriptionService>(sp =>
    sp.GetRequiredService<FoundryLocalTranscriptionService>());
builder.Services.AddSingleton<IWhisperReadiness>(sp =>
    sp.GetRequiredService<FoundryLocalTranscriptionService>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<FoundryLocalTranscriptionService>());

builder.Services.AddSingleton<TelegramRateLimiter>();
builder.Services.AddSingleton<TelegramMessageSender>();
builder.Services.AddSingleton<TelegramFileService>();
builder.Services.AddSingleton<ChatActionService>();
builder.Services.AddSingleton<TelegramBotState>();
builder.Services.AddSingleton<TelegramBotService>();

// gRPC client to the system silo. The "https+http://system" scheme is resolved
// by Aspire's ServiceDiscovery (configured by AddServiceDefaults) using the
// system silo's Aspire resource name + endpoints injected via WithReference.
builder.Services.AddGrpcClient<global::Ino.Grpc.Ino.InoClient>(o =>
{
    o.Address = new Uri("https+http://system");
});

// Webhook + WebApp URL resolution + bot command registration. Hosted last
// so all dependencies are constructed before its ExecuteAsync runs.
builder.Services.AddHostedService<WebhookSetupService>();

// CORS open for the Flutter bundle that loads from this host's origin —
// Flutter calls gRPC-Web back to the system silo (cross-origin), but any
// asset fetch + same-origin RPC needs CORS to be present.
builder.Services.AddCors(o => o.AddPolicy("InoTelegram", p => p
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader()));

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors("InoTelegram");

// Static files for the Flutter bundle. wwwroot/ holds index.html + Dart-
// compiled JS + CanvasKit shaders, populated by the BuildFlutterWebForTelegram
// MSBuild target on every dotnet build.
var wwwroot = ResolveWwwroot(app);
if (wwwroot is not null)
{
    // .riv isn't in ASP.NET Core's default mime map, so without an explicit
    // mapping the Rive assets the persona orb loads return 404 (mirrors the
    // kernel silo's InoGrpcHostingExtensions).
    var contentTypes = new FileExtensionContentTypeProvider();
    contentTypes.Mappings[".riv"] = "application/octet-stream";
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wwwroot),
        ContentTypeProvider = contentTypes,
    });
}

// /config.json — runtime client configuration the Flutter bundle reads on
// boot. The "GrpcEndpoint" tells Flutter where to make its chat RPCs; we
// route to the system silo over service discovery so the mini-app loaded
// inside Telegram talks to the same kernel as the system-silo-hosted
// surface. Mirrors Ino.Gateway.Grpc.ConfigEndpoint at a slim profile —
// just enough for the mini-app to bootstrap.
app.MapGet("/config.json", (HttpContext ctx, IConfiguration cfg) =>
{
    var systemAddress = cfg["services:system:system-http:0"]
        ?? cfg["services:system:https:0"]
        ?? cfg["services:system:http:0"]
        ?? "/"; // fallback — same-origin, won't reach silo but keeps Flutter from crashing
    var payload = new
    {
        grpcEndpoint = systemAddress,
        otlpEndpoint = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}/otlp",
        healthEndpoint = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}/health",
        transportSecure = ctx.Request.IsHttps,
        retryPolicy = new
        {
            maxRetries = 3,
            initialBackoffMs = 200,
            maxBackoffMs = 5_000,
            timeoutMs = 30_000,
        },
        version = "0.1.0",
    };
    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    ctx.Response.ContentType = "application/json; charset=utf-8";
    return ctx.Response.WriteAsync(json);
});

// OTLP traces/metrics/logs accept-and-discard. The system silo runs the
// "real" forward to the Aspire dashboard; the Telegram host accepts the
// payload so Flutter's batch exporter doesn't retry-thrash, but doesn't
// re-encode + forward (the kernel's OTLP proxy already covers that path
// when Flutter is loaded from the system silo). A future slice can mirror
// the full forward when notifications + persona telemetry from this host
// matter for ops.
app.MapPost("/otlp/v1/traces",  (HttpContext ctx) => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });
app.MapPost("/otlp/v1/metrics", (HttpContext ctx) => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });
app.MapPost("/otlp/v1/logs",    (HttpContext ctx) => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

// /webhook — Telegram POSTs Update payloads here. Optional X-Telegram-Bot-API-
// Secret-Token header is checked when WebhookSecretToken is configured.
// Update processing is fire-and-forget so we ack the POST in <50ms; long-running
// transcription + gRPC chat happens off the request thread.
app.MapPost("/webhook", async (
    HttpContext context,
    TelegramBotService botService,
    IOptions<TelegramBotOptions> options,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var secret = options.Value.WebhookSecretToken;
    if (!string.IsNullOrWhiteSpace(secret))
    {
        var header = context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (!string.Equals(header, secret, StringComparison.Ordinal))
            return Results.Unauthorized();
    }

    var update = await context.Request.ReadFromJsonAsync<Update>(ct);
    if (update is null) return Results.BadRequest();

    _ = Task.Run(async () =>
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await botService.HandleUpdateAsync(update, cts.Token); }
        catch (OperationCanceledException) { logger.LogWarning("Update processing timed out after 5 minutes"); }
        catch (Exception ex) { logger.LogError(ex, "Background update processing failed"); }
    }, ct);
    return Results.Ok();
});

// SPA fallback for client-side routes (GoRouter on the Flutter side picks up
// "?q=…" deep links). Must register AFTER the explicit endpoints above so
// /config.json + /otlp/v1/* + /webhook take precedence over /index.html.
if (wwwroot is not null)
{
    var indexPath = Path.Combine(wwwroot, "index.html");
    app.MapFallback(async ctx =>
    {
        if (!File.Exists(indexPath))
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        ctx.Response.ContentType = "text/html";
        await ctx.Response.Body.WriteAsync(await File.ReadAllBytesAsync(indexPath));
    });
}

await app.RunAsync();

// Aspire rebuild changes the host's ContentRootPath between initial launch
// and `rebuild`, so probe ContentRoot, current dir, and the assembly
// base directory in order — same trick Ino.System.Host's gateway uses.
static string? ResolveWwwroot(WebApplication app)
{
    var candidates = new[]
    {
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        Path.Combine(Environment.CurrentDirectory, "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    };
    return candidates.FirstOrDefault(Directory.Exists);
}
