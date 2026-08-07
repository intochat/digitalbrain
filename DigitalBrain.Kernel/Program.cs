using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainSilo(silo => silo.AddDigitalBrain());
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapChatStreams();
app.MapShellStreams();
app.MapAuthorizationStreams();
app.MapBrainTopology();
app.MapOAuthCallback();
app.Run();
