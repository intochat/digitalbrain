using DigitalBrain.Platform;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Aspire.Server;
using DigitalBrain.Testing.E2E;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Entry point for the module runtime process that ModuleTestHost launches. It runs against the
// test assembly's dependency closure, so every selected module assembly is already resolvable.
var builder = WebApplication.CreateBuilder(args);
var referenceComposition = builder.Configuration.GetValue<bool>("DigitalBrain:Testing:ReferenceComposition");
if (referenceComposition)
{
    builder.AddReferenceTelemetry();
    builder.Configuration["DigitalBrain:Identity:CookieName"] ??= "digitalbrain.reference.session";
}
builder.AddDigitalBrainServer(server =>
{
    server.UseAzureStorage();
    if (referenceComposition)
    {
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
    }
    else
    {
        var names = System.Text.Json.JsonSerializer.Deserialize<string[]>(builder.Configuration["DigitalBrain:Testing:ModuleTypes"] ?? "[]")!;
        foreach (var name in names)
        {
            var type = Type.GetType(name, throwOnError: true)!;
            server.AddModule(DigitalBrain.Kernel.ModuleIdentity.Get(type), () => (DigitalBrain.Kernel.IModule)Activator.CreateInstance(type)!);
        }
    }
});
builder.AddDigitalBrainPlatform();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.UsePlatformHttp();
app.MapDigitalBrainModules();
app.MapDigitalBrainPlatform();
app.MapHealthChecks(ModuleHostEndpoints.Health);
app.MapGet(ModuleHostEndpoints.Process, () => Environment.ProcessId);
await app.RunAsync();
