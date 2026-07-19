using DigitalBrain;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo => silo.AddDigitalBrain().AddDigitalBrainJournalStorage(builder.Configuration));
builder.Services.AddDigitalBrainModels(builder.Configuration);

var app = builder.Build();

app.UseDigitalBrainDevTools();
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
