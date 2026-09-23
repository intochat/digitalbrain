using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.ClickHouse;
using DigitalBrain.Coding;
using DigitalBrain.Behavior;
using DigitalBrain.Behavior.Aspire.Hosting;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Memory;
using DigitalBrain.Microsoft.Aspire;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Microsoft.DotNet;
using DigitalBrain.Microsoft.Roslyn;
using DigitalBrain.MyData;
using DigitalBrain.Salesforce;
using DigitalBrain.Supabase;
using DigitalBrain.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var testing = builder.Configuration.GetValue<bool>("DigitalBrain:Testing:Enabled");
var profile = builder.Configuration[ProductSurfaceResources.ProfileKey] ?? ProductSurfaceResources.DeveloperProfile;
var developerProfile = !string.Equals(profile, ProductSurfaceResources.ProductProfile, StringComparison.OrdinalIgnoreCase);
var repositories = builder.Configuration.GetSection("DigitalBrain:Microsoft:GitHub:Repositories")
    .Get<Dictionary<string, GitHubRepositoryDeclaration>>() ?? [];
var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules, persistentStorage: !testing)
    .WithModule<AIModule>(ai =>
    {
        // Capture model inputs and outputs during local development runs so GenAI
        // traces in Aspire show the actual conversation and tool activity. An
        // explicit configuration value still controls the behavior in every environment.
        ai.ConfigureOptions<AIOptions>(options =>
            options.Telemetry.EnableSensitiveData = builder.Configuration.GetValue<bool?>(
                $"{AIOptions.SectionName}:Telemetry:EnableSensitiveData")
                ?? (builder.Environment.IsDevelopment() && builder.ExecutionContext.IsRunMode),
            "Telemetry.EnableSensitiveData");

        ai.WithLlm<IGpt56Luna>()
            .WithDefaultLlm<IGemma4>()
            .WithDefaultEmbedding<ITextEmbedding3Small>()
            .WithVoiceToText<IWhisperLargeV3Turbo>()
            .WithTavilySearch();
    })
    .WithModule<MemoryModule>(memory => memory.WithQdrant())
    .WithModule<ClickHouseModule>(database => database.WithClickHouse(options => options.WithSeed("leads")))
    .WithModule<SupabaseModule>(database => database.WithConnection("supabase"))
    .WithModule<TimeModule>()
    .WithModule<MyDataModule>()
    .WithModule<GmailModule>(gmail => gmail.WithGmail())
    .WithModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
    .WithModule<GitHubModule>(github => github.WithGitHubRepositories(repositories))
    .WithModule<FlutterModule>(flutter => flutter.RunDesktopApp());

if (developerProfile)
{
    // Developer-only surfaces: the Windows executor, C# compilation, the behavior runtime and the
    // self-referential Aspire project path. The product profile composes only modules with a user path.
    digitalBrain
        .WithModule<AspireModule>(aspire => aspire
            .WithAspire(Path.Combine(builder.AppHostDirectory, "IntoChat.AppHost.csproj")))
        .WithModule<RoslynModule>()
        .WithModule<DotNetModule>()
        .WithModule<CodingModule>(coding => coding.WithSolution(Path.GetFullPath(
            Path.Combine(builder.AppHostDirectory, "..", "..", "..", "..", "DigitalBrain.slnx"))))
        .WithModule<BehaviorModule>(behavior =>
        {
            if (builder.Configuration["IntoChat:BehaviorAuthoring:Root"] is { Length: > 0 } root)
            { behavior.WithLocalExecution(root); }
        });
}

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
        // No default host filesystem root: the Files app stays off unless a root is configured.
        var downloads = builder.Configuration["IntoChat:LocalFiles:Roots:downloads"];
        if (downloads is not null) { context.EnvironmentVariables["IntoChat__LocalFiles__Roots__downloads"] = downloads; }
        var assets = builder.Configuration["IntoChat:LocalFiles:AssetDirectory"];
        if (assets is not null) { context.EnvironmentVariables["IntoChat__LocalFiles__AssetDirectory"] = assets; }
        if (builder.Configuration["IntoChat:Assistant:Model"] is { Length: > 0 } assistantModel)
        { context.EnvironmentVariables["IntoChat__Assistant__Model"] = assistantModel; }
        foreach (var setting in new[] { "AllowActivation", "ModelProfile" })
        {
            if (builder.Configuration["IntoChat:BehaviorAuthoring:" + setting] is { } value)
            { context.EnvironmentVariables["IntoChat__BehaviorAuthoring__" + setting] = value; }
        }
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