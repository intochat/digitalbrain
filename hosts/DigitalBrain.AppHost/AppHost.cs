using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var journal = storage.AddBlobs("journal");

builder.AddProject<Projects.Brain_Kernel_Host>("kernel")
    .WithReference(journal)
    .WaitFor(storage);

builder.Build().Run();
