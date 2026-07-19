using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);
builder.AddDigitalBrainKernel("brain");
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("DigitalBrain.Neuron"));
if (!string.IsNullOrWhiteSpace(
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    builder.Services.AddOpenTelemetry().UseOtlpExporter();
if (builder.Environment.IsDevelopment())
    builder.AddDigitalBrainDashboardSilo();

using var host = builder.Build();
await host.RunAsync();
