using DigitalBrain.Postgres;
using DigitalBrain.Microsoft.Playwright;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Apps;
using DigitalBrain.Specs;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.ClickHouse;
using DigitalBrain.Coding;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Identity;
using DigitalBrain.Memory;
using DigitalBrain.Qdrant;
using DigitalBrain.Microsoft.Aspire;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Microsoft.DotNet;
using DigitalBrain.Microsoft.Roslyn;
using DigitalBrain.Compute;
using DigitalBrain.Registry;
using DigitalBrain.Sdk.Connectors;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Salesforce;
using DigitalBrain.Supabase;
using DigitalBrain.Time;
using IntoChat.AppHost;
using DigitalBrain.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var testing = builder.Configuration.GetValue<bool>("DigitalBrain:Testing:Enabled");
var profile = builder.Configuration[ProductSurfaceResources.ProfileKey] ?? ProductSurfaceResources.DeveloperProfile;
var hosted = HostedProfile.IsHosted(profile, builder.Configuration);
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", ".."));
var repositories = builder.Configuration.GetSection("DigitalBrain:Microsoft:GitHub:Repositories")
    .Get<Dictionary<string, GitHubRepositoryDeclaration>>() ?? [];
var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules, persistentStorage: !testing)
    .WithModule<AIModule>(ai =>
    {
        ai.ConfigureOptions<AIOptions>(options =>
            options.Telemetry.EnableSensitiveData = true);

        ai.WithLlm<IGpt56Luna>()
            .WithDefaultLlm<IGemma4>()
            .WithDefaultEmbedding<ITextEmbedding3Small>()
            .WithVoiceToText<IWhisperLargeV3Turbo>()
            .WithTavilySearch();
    })
    .WithModule<QdrantModule>(qdrant => qdrant.WithHostedQdrant())
    .WithModule<MemoryModule>()
    .WithModule<ClickHouseModule>(database => database.WithClickHouse(options => options.WithSeed("leads")))
    .WithModule<SupabaseModule>(database => database.WithConnection("supabase"))
    .WithModule<PostgresModule>(database => database.WithPostgres(options => { options.DatabaseName = "customer-research"; options.PersistentStorage = !testing; }))
    .WithModule<PlaywrightModule>()
    .WithModule<TimeModule>()
    .WithModule<SecretsModule>()
    .WithModule<ConnectorModule>()
    .WithModule<IdentityModule>()
    .WithModule<FilesModule>()
    .WithModule<GmailModule>(gmail => gmail.WithGmail())
    .WithModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
    .WithModule<GitHubModule>(github => github.WithGitHubRepositories(repositories))
    .WithModule<FlutterModule>(flutter => flutter.RunDesktopApp())
    .WithModule<ComputeModule>()
    .WithModule<RegistryModule>()
    .WithModule<SpecsModule>()
    .WithModule<AppsModule>()
    .WithModule<AspireModule>()
    .WithModule<RoslynModule>()
    .WithModule<DotNetModule>()
    .WithModule<CodingModule>(coding => coding.WithSolution(Path.Combine(repositoryRoot, "DigitalBrain.slnx")))
    .WithModule<CSharpModule>(csharp => csharp.WithSandbox(repositoryRoot));

var clusterId = builder.Configuration["Orleans:ClusterId"]
    ?? (builder.Environment.IsDevelopment() ? $"digitalbrain-{Guid.NewGuid():N}" : null);
// Deployment membership can change; the logical service must survive restarts.
// Preserve an explicitly configured historical cluster/service identity.
var serviceId = builder.Configuration["Orleans:ServiceId"]
    ?? builder.Configuration["Orleans:ClusterId"] ?? "intochat";

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
        if (builder.Configuration["IntoChat:Assistant:Model"] is { Length: > 0 } assistantModel)
        { context.EnvironmentVariables["IntoChat__Assistant__Model"] = assistantModel; }
        if (builder.Configuration["DigitalBrain:CSharp:AllowActivation"] is { } allowActivation)
        { context.EnvironmentVariables["DigitalBrain__CSharp__AllowActivation"] = allowActivation; }
        if (clusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = clusterId;
        }
        context.EnvironmentVariables["Orleans__ServiceId"] = serviceId;
        foreach (var key in new[] { "IntoChat:DataProtection:Certificate", "IntoChat:DataProtection:CertificatePassword" })
        {
            if (builder.Configuration[key] is { Length: > 0 } value)
            { context.EnvironmentVariables[key.Replace(":", "__", StringComparison.Ordinal)] = value; }
        }
        foreach (var setting in builder.Configuration.GetSection("IntoChat:DataProtection:PreviousCertificates").AsEnumerable())
        {
            if (setting.Value is not null)
            { context.EnvironmentVariables[setting.Key.Replace(":", "__", StringComparison.Ordinal)] = setting.Value; }
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

// Existing volumes remain untouched. SQL is an explicit, read-only migration source.
if (builder.Configuration.GetValue<bool>("DigitalBrain:Compute:ImportLegacy"))
{
    runtime.WithEnvironment("DigitalBrain__Compute__ImportLegacy", "true");
    var legacyConnection = builder.Configuration[ComputeModule.ConnectionStringKey]
        ?? builder.Configuration.GetConnectionString(ComputeModule.LedgerConnectionName);
    if (!string.IsNullOrWhiteSpace(legacyConnection))
    { runtime.WithEnvironment("DigitalBrain__Compute__ConnectionString", legacyConnection); }
    else if (builder.Configuration["DigitalBrain:Compute:UsageDirectory"] is { Length: > 0 } usageDirectory)
    { runtime.WithEnvironment("DigitalBrain__Compute__UsageDirectory", usageDirectory); }
    else
    {
        var computeServer = builder.AddPostgres("compute-postgres");
        if (!testing) { computeServer.WithDataVolume(); }
        var computeLedger = computeServer.AddDatabase("compute-database", ComputeModule.LedgerConnectionName);
        runtime.WithReference(computeLedger, ComputeModule.LedgerConnectionName).WaitFor(computeLedger);
    }
}

if (testing)
{
    // Null arguments to WithHttpEndpoint retain ports from launchSettings.json.
    // Clear both inherited ports so each test deployment receives its own endpoint.
    runtime.WithEndpoint("http", endpoint => { endpoint.Port = null; endpoint.TargetPort = null; });
}

if (hosted)
{
    // Product-only hosted deployment: managed identity and Key Vault are wired by configuration.
    HostedProfile.Apply(runtime, builder.Configuration);
}

builder.Build().Run();
