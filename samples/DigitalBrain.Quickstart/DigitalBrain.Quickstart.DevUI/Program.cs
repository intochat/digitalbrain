using DigitalBrain.DevTools;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainDevUI("brain");
var app = builder.Build();
app.MapHealthChecks("/health");
app.MapDigitalBrainDevUI();

if (args.Contains("--startup-contract", StringComparer.Ordinal))
{
    Console.WriteLine("development-host:ok");
    return;
}

await app.RunAsync();
