using DigitalBrain.DevTools;
using DigitalBrain.Kernel;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration)
    .AddDigitalBrainDevTools(builder.Environment));
builder.Services.AddDigitalBrainModels(builder.Configuration);

var app = builder.Build();

app.MapDigitalBrainDevTools(app.Environment);
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
