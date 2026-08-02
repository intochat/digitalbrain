using DigitalBrain.Aspire;
using DigitalBrain.Behaviors;
using DigitalBrain.Kernel;
using DigitalBrain.OS.McpHost;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddKeyedAzureTableServiceClient("brain-reminders");
builder.Services.AddBehaviorBrokerAuthentication(builder.Configuration, builder.Environment);
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));
builder.AddDigitalBrainOwner(activateOnStart: false);
builder.Services.AddDigitalBrainMcpServer();

var app = builder.Build();
app.UseBehaviorBrokerAuthentication();
app.MapDefaultEndpoints();
app.MapBehaviorProtectedPayloadBroker();
app.MapBehaviorProtectedTriggerBroker();
app.MapBehaviorTaskOperationBroker();
app.MapBehaviorDispatchBroker();
app.MapMcpHost();
app.Run();
