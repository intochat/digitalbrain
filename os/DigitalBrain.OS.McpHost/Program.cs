using DigitalBrain.Aspire;
using DigitalBrain.OS.McpHost;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();
builder.Services.AddDigitalBrainMcpServer();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcpHost();
app.Run();
