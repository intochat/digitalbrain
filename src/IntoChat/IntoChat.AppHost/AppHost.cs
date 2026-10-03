using Aspire.Hosting;
using DigitalBrain.AI;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.AI.OpenRouter;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.ClickHouse;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Microsoft.Aspire;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Postgres;
using DigitalBrain.Qdrant;
using DigitalBrain.Salesforce;
using DigitalBrain.Supabase;
using IntoChat;

var builder = DistributedApplication.CreateBuilder(args);
var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules, serviceId: "intochat", options: new() { UseAzureStorage = true, Dashboard = true })
    .WithHttpIdentity(new("intochat.session", "IntoChat.v1", IntoChatConfiguration.ProtectionContainerName))
    .WithModule<DigitalBrain.AI.Aspire.Hosting.AIModuleHosting, AIOptions>(ai =>
    {
        ai.Telemetry.EnableSensitiveData = true;

        ai.WithLlm<IGpt56Luna>();
        ai.WithDefaultLlm<IDeepSeekV41Flash>();
        ai.WithDefaultEmbedding<ITextEmbedding3Small>();
        ai.WithVoiceToText<IWhisperLargeV3Turbo>();
        ai.WithTavilySearch();
    })
    .WithModule<DigitalBrain.Qdrant.Aspire.Hosting.QdrantModuleHosting, QdrantModuleOptions>(qdrant => qdrant.WithHostedQdrant())
    .WithModule<DigitalBrain.ClickHouse.Aspire.Hosting.ClickHouseModuleHosting, ClickHouseModuleOptions>(database => database.WithClickHouse())
    .WithModule<DigitalBrain.Supabase.Aspire.Hosting.SupabaseModuleHosting, SupabaseModuleOptions>(database => database.WithConnection("supabase"))
    .WithModule<DigitalBrain.Postgres.Aspire.Hosting.PostgresModuleHosting, PostgresModuleOptions>(database => database.WithPostgres(options => options.DatabaseName = "digitalbrain"))
    .WithModule("playwright")
    .WithModule("time")
    .WithModule("files")
    .WithModule<DigitalBrain.Google.Gmail.GmailModuleHosting, GmailModuleOptions>(gmail => gmail.WithGmail())
    .WithModule<DigitalBrain.Salesforce.Aspire.Hosting.SalesforceModuleHosting, SalesforceModuleOptions>(salesforce => salesforce.WithHostedMcp())
    .WithModule<DigitalBrain.Microsoft.GitHub.GitHubModuleHosting>()
    .WithModule<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting, FlutterModuleOptions>(flutter => flutter.RunDesktopApp())
    .WithModule("compute")
    .WithModule("registry")
    .WithModule("specs")
    .WithModule("apps")
    .WithModule("assistant")
    .WithModule<DigitalBrain.Microsoft.Aspire.AspireModuleHosting>()
    .WithModule<DigitalBrain.Microsoft.CSharp.CSharpModuleHosting>()
    .WithModule<DigitalBrain.Microsoft.CSharp.CSharpAuthoringModuleHosting>();

var cookieProtection = digitalBrain.AddBlobContainer(IntoChatConfiguration.ProtectionContainerName);

var runtime = builder.AddProject<Projects.IntoChat>(ProductSurfaceResources.IntoChat)
    .WithReference(digitalBrain)
    .WaitFor(cookieProtection)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: "http",
        isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http");

builder.AddProject<Projects.DigitalBrain_Mcp>("digitalbrain-mcp")
    .WithReference(digitalBrain.AsClient(shareHttpIdentity: true))
    .WithHttpEndpoint(port: ProductSurfaceResources.McpHttpPort, name: "http", isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(runtime);

builder.Build().Run();
