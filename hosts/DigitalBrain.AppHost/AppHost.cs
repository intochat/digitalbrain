using DigitalBrain.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var journal = storage.AddBlobs("journal");

var brain = builder.AddBrain("brain")
    .WithDevelopmentStores();

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain)
    .WithReference(journal);

builder.Build().Run();
