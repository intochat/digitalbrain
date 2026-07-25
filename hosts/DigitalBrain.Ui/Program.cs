using DigitalBrain.Aspire;
using DigitalBrain.Ui;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();
builder.Services.AddUiEdgeServices();

var app = builder.Build();
app.MapUiHost();
app.Run();
