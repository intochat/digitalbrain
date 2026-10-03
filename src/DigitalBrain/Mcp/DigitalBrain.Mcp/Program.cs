using DigitalBrain.Client.Orleans;
using DigitalBrain.Aspire.Client;
using DigitalBrain.Mcp;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainClient(useAzureClustering: true);
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<NeuronTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.Run();
