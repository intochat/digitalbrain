using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Core.Behavior;
using DigitalBrain.Mcp;
using IntoChat.ServiceDefaults;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
builder.UseOrleansClient(DigitalBrainRuntime.AddClient);
builder.Services.AddSingleton<BehaviorService>();
builder.Services.AddSingleton<BehaviorTools>();
builder.Services.AddDigitalBrainMcp()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<BehaviorTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapDigitalBrainMcp("/mcp");
app.Run();
