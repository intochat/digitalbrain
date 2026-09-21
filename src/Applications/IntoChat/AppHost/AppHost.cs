using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Behaviors;
using DigitalBrain.ClickHouse;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Excel;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Memory;
using DigitalBrain.Microsoft;
using DigitalBrain.Salesforce;
using DigitalBrain.Supabase;
using DigitalBrain.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var testing = builder.Configuration.GetValue<bool>("DigitalBrain:Testing:Enabled");
var repositories = builder.Configuration.GetSection("DigitalBrain:Microsoft:GitHub:Repositories")
    .Get<Dictionary<string, GitHubRepositoryDeclaration>>() ?? [];
var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules, persistentStorage: !testing)
    .WithModule<AIModule>(ai => ai
        .WithLlm<IGpt56Luna>()
        .WithDefaultLlm<IGemma4>()
        .WithDefaultEmbedding<ITextEmbedding3Small>()
        .WithVoiceToText<IWhisperLargeV3Turbo>()
        .WithTavilySearch())
    .WithModule<MemoryModule>(memory => memory.WithQdrant())
    .WithModule<ClickHouseModule>(database => database.WithClickHouse(options => options.WithSeed("leads")))
    .WithModule<SupabaseModule>(database => database.WithConnection("supabase"))
    .WithModule<TimeModule>()
    .WithModule<ExcelModule>()
    .WithModule<GoogleModule>(google => google.WithGmail())
    .WithModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
    .WithModule<MicrosoftModule>(microsoft => microsoft
        .WithAspire(Path.Combine(builder.AppHostDirectory, "IntoChat.AppHost.csproj"))
        .WithGitHubRepositories(repositories))
    .WithModule<CodingModule>(coding => coding.WithSolution(Path.GetFullPath(
        Path.Combine(builder.AppHostDirectory, "..", "..", "..", "..", "DigitalBrain.slnx"))))
    .WithModule<FlutterModule>(flutter => flutter.RunDesktopApp())
    // Existing demo behavior dependency; this is part of the application, not injected by tests.
    .WithModule<TestTwitterModule>();

var clusterId = builder.Configuration["Orleans:ClusterId"]
    ?? (builder.Environment.IsDevelopment() ? $"digitalbrain-{Guid.NewGuid():N}" : null);

var runtime = builder.AddProject<Projects.IntoChat>(ProductSurfaceResources.IntoChat)
    .WithReference(digitalBrain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithHttpEndpoint(
        port: testing ? null : ProductSurfaceResources.UiHttpPort,
        name: "http",
        isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .AsPrimaryBrain()
    .WithUrlForEndpoint(
        "http",
        endpoint => new ResourceUrlAnnotation
        {
            Url = "/orleans",
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        })
    .WithEnvironment(context =>
    {
        var downloads = builder.Configuration["IntoChat:LocalFiles:Roots:downloads"];
        if (downloads is null && !testing) { downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); }
        if (downloads is not null) { context.EnvironmentVariables["IntoChat__LocalFiles__Roots__downloads"] = downloads; }
        var assets = builder.Configuration["IntoChat:LocalFiles:AssetDirectory"];
        if (assets is not null) { context.EnvironmentVariables["IntoChat__LocalFiles__AssetDirectory"] = assets; }
        if (clusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = clusterId;
            context.EnvironmentVariables["Orleans__ServiceId"] = clusterId;
        }

        // Browser shell (aspire run and Playwright e2e) is a different origin than the kernel.
        // IsRunMode is false under DistributedApplicationTestingBuilder, so the origin must
        // always be advertised — not only when `aspire run` is driving the host.
        var flutter = digitalBrain.GetModuleConfiguration<FlutterModule>().GetSection("DigitalBrain:Flutter:Hosting").Get<FlutterHostingOptions>()!;
        if (flutter.Kind == FlutterHostKind.Web)
        {
            context.EnvironmentVariables["DigitalBrain__Cors__AllowedOrigin"] =
                builder.CreateResourceBuilder<ExecutableResource>(flutter.ResourceName).GetEndpoint("http");
        }

    });

if (testing)
{
    // Null arguments to WithHttpEndpoint retain ports from launchSettings.json.
    // Clear both inherited ports so each test deployment receives its own endpoint.
    runtime.WithEndpoint("http", endpoint => { endpoint.Port = null; endpoint.TargetPort = null; });
}

builder.Build().Run();