using DigitalBrain.DevTools;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainDashboard("brain");
var app = builder.Build();
app.MapDigitalBrainDashboard();

if (args.Contains("--startup-contract", StringComparer.Ordinal))
{
    Console.WriteLine("development-host:ok");
    return;
}

await app.RunAsync();
