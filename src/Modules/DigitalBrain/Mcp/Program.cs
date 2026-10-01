using DigitalBrain.Client;
using DigitalBrain.Mcp;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainClient();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<NeuronTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.Run();
