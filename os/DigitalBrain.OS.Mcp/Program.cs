using DigitalBrain.Aspire;
using DigitalBrain.OS.Mcp;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainClient();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(static options => options.Stateless = true)
    .WithTools<ChatTools>()
    .WithTools<IntrospectionTools>()
    .WithTools<BehaviorTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp(McpSurface.Path);
app.Run();
