using DigitalBrain.Aspire;
using DigitalBrain.Ui;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();
builder.Services.AddUIEdgeServices();

var app = builder.Build();
app.MapUIHost();
app.Run();
