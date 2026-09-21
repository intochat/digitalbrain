using DigitalBrain.Aspire;
using DigitalBrain.Sdk;

var builder = WebApplication.CreateBuilder(args);
foreach (var name in builder.Configuration.GetSection("DigitalBrain:Modules").Get<string[]>() ?? [])
{
    _ = Type.GetType(name, throwOnError: true);
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
app.MapHealthChecks("/health");
app.MapGet("/process", () => Environment.ProcessId);
await app.RunAsync();
