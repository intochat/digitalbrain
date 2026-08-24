using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Auth;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddKernelCors();
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

var app = builder.Build();
app.UseKernelCors();
app.UseBasicAuthGate();
app.MapDefaultEndpoints();
app.MapOwnerCommands();
app.MapChatVoice();
app.MapChatStreams();
app.MapKitEntities();
app.MapSurfaceStreams();
app.MapOrleansDashboard("/orleans");
app.Run();
