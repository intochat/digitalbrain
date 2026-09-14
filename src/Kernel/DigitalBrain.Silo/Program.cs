using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Aspire;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using Microsoft.Build.Locator;
using ModelContextProtocol.AspNetCore;
using Orleans.Dashboard;

// No other module may load a Microsoft.Build assembly before the locator registers the real MSBuild.
MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

// Both slots share a ServiceId so grain state, journals and reminders survive a swap; membership must be
// fresh or the new silo stalls on the previous one's dead row (R5.3). A slot that was given no ClusterId
// mints one per start; the in-memory source is added last, so it wins precedence (spike S2).
if (string.IsNullOrWhiteSpace(builder.Configuration[ActiveSlotNames.ClusterIdKey])
    && builder.Configuration[ActiveSlotNames.SlotKey] is { Length: > 0 } slot)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [ActiveSlotNames.ClusterIdKey] = ClusterIdMinting.Mint(slot, TimeProvider.System.GetUtcNow()),
    });
}

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
app.MapSlotEndpoints();
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
