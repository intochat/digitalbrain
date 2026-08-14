using DigitalBrain.Aspire;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainClient();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
