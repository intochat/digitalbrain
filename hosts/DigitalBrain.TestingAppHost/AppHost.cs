using DigitalBrain.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();

var brain = builder.AddBrain("brain")
    .WithAzureStorage(storage);

var probeBrain = builder.AddBrain("probe")
    .WithAzureStorage(storage);

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);

builder.AddProject<Projects.DigitalBrain_ProbeHost>("probe")
    .WithReference(probeBrain);

builder.Build().Run();
