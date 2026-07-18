using DigitalBrain.Kernel;
using Brain.Kernel.Host;
using Google;
using Salesforce;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddDigitalBrainKernel("brain");
builder.UseOrleans(silo =>
{
    silo.AddGoogle();
    silo.AddSalesforce();
});
var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
