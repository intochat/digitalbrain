using DigitalBrain.Aspire;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.UI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();
builder.Services.AddUiHttpServices();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapDefaultEndpoints();
app.MapUIHost();
app.Run();
