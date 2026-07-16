using Brain.Kernel;
using Brain.Modules.Ai;
using Brain.Modules.Workspace;
using DigitalBrain.ServiceDefaults;
using Orleans.Journaling;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddJournalStorage();
    silo.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
    silo.AddBrainKernel(new ChatKind());
    silo.AddBrainAi(builder.Configuration);
});
var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
