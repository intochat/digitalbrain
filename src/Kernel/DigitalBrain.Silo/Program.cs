using DigitalBrain.Aspire;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using ModelContextProtocol.AspNetCore;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddKernelCors();
builder.Services.AddAuthentication();
builder.Services.AddSingleton<SessionStreamOptions>();
builder.Services.AddDigitalBrainMcp()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);

var app = builder.Build();

app.UseKernelCors();
// Module surfaces (browser OAuth callbacks) carry their own one-use request guards and must
// run before authentication and the Basic gate.
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseBasicAuthGate();
app.UseSessionNeuron();
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.MapDigitalBrainMcp("/mcp");
app.MapChatEndpoints();
app.MapChatVoiceEndpoints();
app.MapSurfaceEndpoints();
app.MapKitEndpoints();
app.MapActivityEndpoints();
app.MapGraphEndpoints();

app.Run();
