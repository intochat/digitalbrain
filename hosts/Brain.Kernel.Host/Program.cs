using Brain.Kernel;
using Brain.Kernel.Host;
using Orleans.Journaling;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddJournalStorage();
    var journalConnectionString = builder.Configuration.GetConnectionString("journal")
        ?? throw new InvalidOperationException("Connection string 'journal' is required for durable journal storage.");
    silo.AddAzureBlobJournalStorage(options =>
    {
        options.ConfigureBlobServiceClient(journalConnectionString);
        options.ContainerName = "journals";
    });
    silo.AddBrainKernel();
});
var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
