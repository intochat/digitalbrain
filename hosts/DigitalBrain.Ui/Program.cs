using DigitalBrain.Aspire;
using DigitalBrain.Ui;

var builder = WebApplication.CreateBuilder(args);

var owner = builder.Configuration["DigitalBrain:Owner"];
if (string.IsNullOrWhiteSpace(owner))
{
    owner = "dev";
}

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient(owner);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapUi();

app.Run();
