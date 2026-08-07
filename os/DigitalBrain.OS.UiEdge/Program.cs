using DigitalBrain.Aspire;
using DigitalBrain.OS.UiEdge;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainClient();
builder.Services.AddUiEdgeServices();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapDefaultEndpoints();
app.MapUiEdgeHost();
app.Run();
