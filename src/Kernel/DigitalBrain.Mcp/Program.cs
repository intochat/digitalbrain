using DigitalBrain.Aspire;
using DigitalBrain.Auth;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainClient();
builder.AddDigitalBrainAuth();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(static options => options.Stateless = true)
    .WithTools<ChatTools>()
    .WithTools<IntrospectionTools>()
    .WithTools<RegistryTools>()
    .WithTools<TimeTools>()
    .WithTools<LibraryBehaviorTools>();

var app = builder.Build();
app.UseDigitalBrainAuth();
app.MapDefaultEndpoints();
app.MapMcp(McpSurface.Path);
app.Run();
