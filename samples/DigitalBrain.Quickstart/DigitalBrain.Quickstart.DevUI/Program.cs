using DigitalBrain.DevTools;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainDevUI("brain");
var app = builder.Build();
app.MapDigitalBrainDevUI();

if (args.Contains("--startup-contract", StringComparer.Ordinal))
{
    Console.WriteLine("development-host:ok");
    return;
}

await app.RunAsync();
