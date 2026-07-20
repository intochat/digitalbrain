using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using DigitalBrain.Modules.AI;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddModule<AIModule>()
    .AddDigitalBrainJournalStorage(builder.Configuration)
    .AddDigitalBrainDevTools(builder.Environment));
builder.Services.AddAIModule();
builder.Services.AddDigitalBrainModels(builder.Configuration);

var app = builder.Build();

app.MapDigitalBrainDevTools(app.Environment);
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
