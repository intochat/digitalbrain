using DigitalBrain.Kernel;

const string HealthPath = "/health";
const string HealthResponse = "healthy";

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddKeyedAzureTableServiceClient("brain-reminders");
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));

var app = builder.Build();
app.MapGet(HealthPath, static () => Results.Ok(HealthResponse));
app.Run();
