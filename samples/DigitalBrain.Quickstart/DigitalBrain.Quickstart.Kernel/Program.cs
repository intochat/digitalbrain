using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddDigitalBrainKernel("brain");
if (builder.Environment.IsDevelopment())
    builder.AddDigitalBrainDashboardSilo();

using var host = builder.Build();
await host.RunAsync();
