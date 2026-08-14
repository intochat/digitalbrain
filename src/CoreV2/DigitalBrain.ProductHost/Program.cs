using DigitalBrain.ProductHost.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainProductHost();

var app = builder.Build();
await app.RunAsync();
