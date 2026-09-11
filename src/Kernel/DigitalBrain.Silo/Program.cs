using DigitalBrain.Aspire;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using ModelContextProtocol.AspNetCore;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddConversationalAgent();
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
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.MapConversationalAgent();
app.MapWorkspaceEndpoints();
app.MapUiEndpoints();
app.MapBrainObservationEndpoints();

// Graph HTTP capabilities are optional; conversation handling stays direct.
// Table tools use UI neurons independently of these graph routes.
if (app.Configuration.GetValue<bool>("DigitalBrain:Graph:Enabled"))
{
    app.UseSessionNeuron();
    app.MapDigitalBrainMcp("/mcp");
    app.MapSurfaceEndpoints();
    app.MapActivityEndpoints();
    app.MapGraphMutationEndpoints();
}

app.Run();
