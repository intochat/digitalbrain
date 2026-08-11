using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddDigitalBrainAuth();
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

var app = builder.Build();
app.UseDigitalBrainAuth();
app.MapDefaultEndpoints();
app.MapAuth();
app.MapOwnerCommands();
app.MapChatStreams();
app.MapSurfaceStreams();
app.MapAuthorizationStreams();
app.MapBrainTopology();
app.MapGraphStreams();
app.MapOAuthCallback();
app.MapOrleansDashboard("/orleans");
app.Run();

public partial class Program;
