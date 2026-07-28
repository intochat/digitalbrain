using DigitalBrain.Aspire;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.UI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();
builder.Services.AddUIEdgeServices();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapUIHost();
app.Run();
