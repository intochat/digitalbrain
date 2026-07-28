using DigitalBrain.Aspire;
using DigitalBrain.OS.Mcp;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.AI;
using Orleans.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient(client =>
{
    client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
        static type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrainMcpTools>()
    .WithTools<DigitalBrainIntrospectionTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcpHost();
app.Run();
