using DigitalBrain.Aspire;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainClient();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<ChatTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp(McpSurface.Path);
app.Run();
