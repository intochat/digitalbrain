using DigitalBrain.Aspire;
using DigitalBrain.Sdk;

var builder = WebApplication.CreateBuilder(args);
foreach (var name in builder.Configuration.GetSection("DigitalBrain:Modules").Get<string[]>() ?? [])
{
    _ = Type.GetType(name, throwOnError: true);
}
builder.AddDigitalBrain();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseModuleHttpSurfaces();
app.MapDigitalBrainModules();
app.MapHealthChecks("/health");
app.MapGet("/process", () => Environment.ProcessId);
await app.RunAsync();
