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
foreach (var moduleTypeName in builder.Configuration.GetSection("DigitalBrain:Modules").Get<string[]>() ?? [])
{
    _ = Type.GetType(moduleTypeName, throwOnError: true);
}
builder.AddDigitalBrainRuntime();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.MapDigitalBrainModules();
app.MapHealthChecks(ModuleHostEndpoints.Health);
app.MapGet(ModuleHostEndpoints.Process, () => Environment.ProcessId);
await app.RunAsync();
