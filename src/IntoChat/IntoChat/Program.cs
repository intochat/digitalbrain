using DigitalBrain.Platform;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Aspire.Server;
using DigitalBrain.Compute;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Sdk;
using IntoChat;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using IntoChat.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDigitalBrainServer(server =>
{
    server.UseAzureStorage().WithDashboard();
    server.AddModule<DigitalBrain.AI.AIModule>("ai");
    server.AddModule<DigitalBrain.ClickHouse.ClickHouseModule>("clickhouse");
    server.AddModule<DigitalBrain.Apps.AppsModule>("apps");
    server.AddModule<DigitalBrain.Assistant.AssistantModule>("assistant");
    server.AddModule<DigitalBrain.Compute.ComputeModule>("compute");
    server.AddModule<DigitalBrain.Registry.RegistryModule>("registry");
    server.AddModule<DigitalBrain.Specs.SpecsModule>("specs");
    server.AddModule<DigitalBrain.Files.FilesModule>("files");
    server.AddModule<DigitalBrain.Flutter.FlutterModule>("flutter");
    server.AddModule<DigitalBrain.Google.Gmail.GmailModule>("gmail");
    server.AddModule<DigitalBrain.Memory.MemoryModule>("memory");
    server.AddModule<DigitalBrain.Microsoft.Aspire.AspireModule>("aspire");
    server.AddModule<DigitalBrain.Microsoft.CSharp.CSharpAuthoringModule>("csharp-authoring");
    server.AddModule<DigitalBrain.Microsoft.CSharp.CSharpModule>("csharp");
    server.AddModule<DigitalBrain.Microsoft.GitHub.GitHubModule>("github");
    server.AddModule<DigitalBrain.Microsoft.Playwright.PlaywrightModule>("playwright");
    server.AddModule<DigitalBrain.Postgres.PostgresModule>("postgres");
    server.AddModule<DigitalBrain.Qdrant.QdrantModule>("qdrant");
    server.AddModule<DigitalBrain.Salesforce.SalesforceModule>("salesforce");
    server.AddModule<DigitalBrain.Supabase.SupabaseModule>("supabase");
    server.AddModule<DigitalBrain.Time.TimeModule>("time");
});
builder.AddDigitalBrainPlatform();
builder.Services.Configure<AssistantOptions>(options =>
{
    options.DisplayName = "IntoChat assistant";
    options.Instructions = "You are the IntoChat workspace assistant. Customer Researcher stores results in Postgres public.customer_research; filter its workspace column to the current workspace id.";
});
builder.Services.Configure<DigitalBrain.Flutter.FlutterModuleOptions>(options => options.AssistantTitle = "IntoChat");
builder.Services.AddShippedApps(typeof(Program).Assembly, "IntoChat.ShippedApps/", publisher: "intochat");
builder.Services.Configure<BuiltInSettingsOptions>(options => options.Package = PackageId.Create("intochat", "settings"));

var app = builder.Build();

app.MapDefaultEndpoints();
app.UsePlatformHttp();
app.MapDigitalBrainModules();
app.MapDigitalBrainPlatform();
app.MapDigitalBrainDashboard();


app.Run();
