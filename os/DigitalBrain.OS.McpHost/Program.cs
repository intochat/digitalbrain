using DigitalBrain.Aspire;
using DigitalBrain.OS.McpHost;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrainMcpTools>()
    .WithTools<DigitalBrainIntrospectionTools>()
    .WithTools<DigitalBrainBehaviorTools>();


var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcpHost();
app.Run();
