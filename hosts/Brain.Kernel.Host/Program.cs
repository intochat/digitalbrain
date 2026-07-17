using Brain.Kernel;
using Brain.Modules.Ai;
using Brain.Modules.Web;
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
    silo.AddBrainKernel(new ChatKind(), new WindowKind(), new FeedKind());
    silo.AddBrainAi(builder.Configuration);
    silo.AddBrainWeb();
});
var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
