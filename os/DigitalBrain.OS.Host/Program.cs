using DigitalBrain.Behaviors;
using DigitalBrain.Kernel;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddKeyedAzureTableServiceClient("brain-reminders");
builder.Services.AddBehaviorBrokerAuthentication(builder.Configuration);
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));

var app = builder.Build();
app.UseBehaviorBrokerAuthentication();
app.MapDefaultEndpoints();
app.MapBehaviorProtectedPayloadBroker();
app.MapBehaviorTaskOperationBroker();
app.Run();
