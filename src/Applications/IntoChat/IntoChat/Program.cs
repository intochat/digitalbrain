using DigitalBrain.Aspire;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using IntoChat.ServiceDefaults;
using IntoChat;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using Orleans.Dashboard;

// No other module may load a Microsoft.Build assembly before the locator registers the real MSBuild.
MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrain();
builder.AddConversationalAgent();
builder.AddKernelCors();
builder.Services.AddAuthentication();
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
if (app.Services.GetRequiredService<IOptions<GraphOptions>>().Value.Enabled)
{
    app.UseSessionNeuron();
    app.MapDigitalBrainMcp("/mcp");
    app.MapSurfaceEndpoints();
    app.MapActivityEndpoints();
    app.MapGraphMutationEndpoints();
}

app.Run();
