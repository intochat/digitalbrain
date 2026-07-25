using DigitalBrain.Kernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddKeyedAzureTableServiceClient("brain-reminders");
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
