using DigitalBrain.Mcp;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDigitalBrainMcp()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);

var app = builder.Build();
app.MapDigitalBrainMcp("/mcp");
app.Run();