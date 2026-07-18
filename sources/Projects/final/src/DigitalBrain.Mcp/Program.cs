using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Orleans", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Orleans", LogLevel.Warning);

builder.AddServiceDefaults();

// IMPORTANT: NO UseOrleansClient, NO client host, NO DigitalBrainCluster registration here.
// All Orleans/brain cluster creation is done *on demand inside tool methods only*.
// This guarantees the MCP HTTP server (Kestrel + MapMcp on the declared 5810) starts and stays healthy
// with zero dependency on the kernel gateway being ready at resource startup time.
// (Aspire WaitFor + kernel http health does not guarantee the TCP Orleans gateway is listening yet.)

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

app.Run();
