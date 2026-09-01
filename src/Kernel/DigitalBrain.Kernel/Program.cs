using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Auth;
using DigitalBrain.Sdk;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.Services.AddAuthentication();
builder.AddKernelCors();
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

var app = builder.Build();
app.UseKernelCors();
// Module surfaces (browser OAuth callbacks) carry their own one-use request guards and must
// run before authentication and the Basic gate.
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseBasicAuthGate();
app.MapDefaultEndpoints();
app.MapOwnerCommands();
app.MapChatVoice();
app.MapChatStreams();
app.MapKitEntities();
app.MapBehaviors();
app.MapSurfaceStreams();
app.MapOrleansDashboard("/orleans");
app.Run();
