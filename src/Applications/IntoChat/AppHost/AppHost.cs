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
using DigitalBrain.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules, serviceId: "intochat")
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
    .WithModule<PostgresModule>(database => database.WithPostgres(options => options.DatabaseName = "customer-research"))
    .WithModule<PlaywrightModule>()
    .WithModule<TimeModule>()
    .WithModule<SecretsModule>()
    .WithModule<ConnectorModule>()
    .WithModule<IdentityModule>()
    .WithModule<FilesModule>()
    .WithModule<GmailModule>(gmail => gmail.WithGmail())
    .WithModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
    .WithModule<GitHubModule>()
    .WithModule<FlutterModule>(flutter => flutter.RunDesktopApp())
    .WithModule<ComputeModule>()
    .WithModule<RegistryModule>()
    .WithModule<SpecsModule>()
    .WithModule<AppsModule>()
    .WithModule<AspireModule>()
    .WithModule<RoslynModule>()
    .WithModule<DotNetModule>()
    .WithModule<CodingModule>()
    .WithModule<CSharpModule>();

var runtime = builder.AddProject<Projects.IntoChat>(ProductSurfaceResources.IntoChat)
    .WithReference(digitalBrain)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
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

builder.Build().Run();
