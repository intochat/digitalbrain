using DigitalBrain.Aspire;
using DigitalBrain.Testing.E2E;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Entry point for the module runtime process that ModuleTestHost launches. It runs against the
// test assembly's dependency closure, so every selected module assembly is already resolvable.
var builder = WebApplication.CreateBuilder(args);
foreach (var moduleTypeName in builder.Configuration.GetSection("DigitalBrain:Modules").Get<string[]>() ?? [])
{
    _ = Type.GetType(moduleTypeName, throwOnError: true);
}
builder.AddDigitalBrain();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseModuleHttpSurfaces();
app.MapDigitalBrainModules();
app.MapHealthChecks(ModuleHostEndpoints.Health);
app.MapGet(ModuleHostEndpoints.Process, () => Environment.ProcessId);
await app.RunAsync();
