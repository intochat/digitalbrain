using DigitalBrain.Aspire;
using DigitalBrain.Mcp;
using Microsoft.Extensions.AI;
using Orleans.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient(client =>
{
    client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
        static type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrainMcpTools>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapMcp("/mcp");

app.Run();
