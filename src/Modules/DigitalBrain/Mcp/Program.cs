using DigitalBrain.Mcp;
using IntoChat.ServiceDefaults;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDigitalBrainMcp()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapDigitalBrainMcp("/mcp");
app.Run();
