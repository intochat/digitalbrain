using DigitalBrain.Aspire;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Core;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainSilo(silo => silo.AddDigitalBrain());
builder.Services.AddBehaviorBrokerAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseBehaviorBrokerAuthentication();
app.MapDefaultEndpoints();
app.MapBehaviorProtectedPayloadBroker();
app.MapBehaviorProtectedTriggerBroker();
app.MapBehaviorTaskOperationBroker();
app.MapBehaviorDispatchBroker();
app.Run();
