using DigitalBrain.Aspire;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk;
using DigitalBrain.Testing.E2E;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Entry point for the module runtime process that ModuleTestHost launches. It runs against the
// test assembly's dependency closure, so every selected module assembly is already resolvable.
var builder = WebApplication.CreateBuilder(args);
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
app.UseModuleHttpSurfaces();
// Stands in for the identity edge's open single-owner posture: stamps the owner on the brain in the route.
app.Use(async (context, next) =>
{
    CallerContextStamper.Stamp(new CallerContext
    {
        PrincipalId = "owner",
        AccountId = "owner",
        BrainId = context.Request.RouteValues["brainId"] as string ?? "owner",
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    });
    await next(context);
});
app.MapGet("/identity/session", () => Results.NoContent());
app.MapDigitalBrainModules();
app.MapHealthChecks(ModuleHostEndpoints.Health);
app.MapGet(ModuleHostEndpoints.Process, () => Environment.ProcessId);
await app.RunAsync();
