using DigitalBrain.Aspire.Server;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.OS;
using DigitalBrain.Platform;
using DigitalBrain.Platform.Identity;
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
        // The reference composition IS the operating system; the catalog is the one list.
        server.AddOperatingSystem();
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
