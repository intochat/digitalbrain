using DigitalBrain.Aspire.Client;
using DigitalBrain.Client.Orleans;
using DigitalBrain.Mcp;
using DigitalBrain.Platform.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainClient(useAzureClustering: true);
builder.Services.AddDigitalBrainMcp();

var app = builder.Build();
app.UseRouting();
app.UseAuthentication();
app.UseAccountSession();
app.MapDefaultEndpoints();
app.MapDigitalBrainMcp();
app.Run();
