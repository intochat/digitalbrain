using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Auth;
using DigitalBrain.Kernel;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddDigitalBrainAuth();
builder.Services.TryAddSingleton<IWorkspaceMembershipGateway, WorkspaceMembershipGateway>();
builder.Services.AddHostedService<DevelopmentBootstrapSeeder>();
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

var app = builder.Build();
app.UseDigitalBrainAuth();
app.MapDefaultEndpoints();
app.MapAuth();
app.MapOwnerCommands();
app.MapChatStreams();
app.MapConversationVoice();
app.MapConversationStreams();
app.MapConversationMessages();
app.MapSurfaceStreams();
app.MapAuthorizationStreams();
app.MapBrainTopology();
app.MapGraphStreams();
app.MapOAuthCallback();
app.MapOrleansDashboard("/orleans");
app.Run();
