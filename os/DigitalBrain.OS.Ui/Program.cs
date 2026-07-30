using DigitalBrain.Aspire;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Flutter.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddDigitalBrainClient();
builder.Services.AddFlutterHttpServices();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapDefaultEndpoints();
app.MapFlutterHttpHost();
app.Run();
