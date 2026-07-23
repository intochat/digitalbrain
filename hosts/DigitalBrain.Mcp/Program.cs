using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

var builder = WebApplication.CreateBuilder(args);

var owner = builder.Configuration["DigitalBrain:Owner"];
if (string.IsNullOrWhiteSpace(owner))
{
    owner = "dev";
}

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.UseOrleansClient(client =>
{
    client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
        type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
});
builder.Services.AddSingleton<IDigitalBrain>(
    services => DigitalBrainClient.Connect(
        services.GetRequiredService<IGrainFactory>(),
        owner));

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrainMcpTools>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapMcp("/mcp");

app.Run();
