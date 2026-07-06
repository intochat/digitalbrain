// DigitalBrain.Mcp - standalone MCP server for DigitalBrain.
// An Orleans CLIENT that exposes cluster interactions as MCP tools (DigitalBrain.Mcp.Tools).
// Requires the kernel cluster (storage + Ollama) to be running - the tools operate on real grains, so there is
// no degraded no-cluster mode (fail-fast). Default transport is stdio for trusted local clients; Aspire sets
// DIGITALBRAIN_MCP_TRANSPORT=http so `aspire mcp` can discover and call the same tools over HTTP.

using DigitalBrain.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (UseHttpTransport())
{
    var builder = WebApplication.CreateBuilder(args);
    ConfigureOrleansClient(builder);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<DigitalBrainReadTools>()
        .WithTools<DigitalBrainMutationTools>();
    AddToolServices(builder.Services);

    var app = builder.Build();
    app.MapMcp("/mcp");
    app.MapGet("/health", () => Results.Ok("DigitalBrain MCP server ready."));
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.AddConsole(consoleLogOptions =>
    {
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    ConfigureOrleansClient(builder);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<DigitalBrainReadTools>()
        .WithTools<DigitalBrainMutationTools>();
    AddToolServices(builder.Services);

    var app = builder.Build();

    await app.StartAsync();
    Console.Error.WriteLine("DigitalBrain MCP server (stdio) started. Ready for tools. Connect via .mcp.json");
    await app.WaitForShutdownAsync();
}

static bool UseHttpTransport() =>
    string.Equals(
        Environment.GetEnvironmentVariable("DIGITALBRAIN_MCP_TRANSPORT"),
        "http",
        StringComparison.OrdinalIgnoreCase);

static void ConfigureOrleansClient(IHostApplicationBuilder builder)
{
    // Orleans client clustering: Aspire injects the provider type. Azure Table in cloud, Redis locally.
    var clusteringProvider = Environment.GetEnvironmentVariable("Orleans__Clustering__ProviderType");
    if (string.Equals(clusteringProvider, "AzureTableStorage", StringComparison.OrdinalIgnoreCase))
    {
        var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
        builder.AddKeyedAzureTableServiceClient(clusteringServiceKey);
    }
    else
    {
        builder.AddKeyedRedisClient("redis");
    }

    builder.UseOrleansClient();
}

static void AddToolServices(IServiceCollection services)
{
    services.AddSingleton<DigitalBrainReadTools>();
    services.AddSingleton<DigitalBrainMutationTools>();
}
