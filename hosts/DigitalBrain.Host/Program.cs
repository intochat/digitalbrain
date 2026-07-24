using DigitalBrain.DevTools;
using DigitalBrain.Kernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddKeyedAzureTableServiceClient("brain-reminders");
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration)
    .AddDigitalBrainDevTools(builder.Environment));

var app = builder.Build();

app.MapDigitalBrainDevTools(app.Environment);
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
