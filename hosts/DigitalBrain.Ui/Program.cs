using DigitalBrain.Aspire;
using DigitalBrain.Ui;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();

var app = builder.Build();
app.MapUiHost();
app.Run();
