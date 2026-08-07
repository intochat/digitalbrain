using DigitalBrain.Aspire;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainSilo(silo => silo.AddDigitalBrain());
builder.Services.AddBehaviorBrokerAuthentication(builder.Configuration, builder.Environment);
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

var app = builder.Build();
app.UseBehaviorBrokerAuthentication();
app.MapDefaultEndpoints();
app.MapBehaviorProtectedPayloadBroker();
app.MapBehaviorProtectedTriggerBroker();
app.MapBehaviorTaskOperationBroker();
app.MapBehaviorDispatchBroker();
app.MapChatStreams();
app.MapShellStreams();
app.MapAuthorizationStreams();
app.MapBrainTopology();
app.MapOAuthCallback();
app.Run();
