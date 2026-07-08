using DigitalBrain.Kernel.Hosting;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.UseDigitalBrainOrleans();
builder.AddDigitalBrainClients();

builder.ConfigureDigitalBrainKestrel();

var app = builder.Build();

app.MapDigitalBrainSetup();
app.MapDigitalBrainHandlers();

app.Run();
