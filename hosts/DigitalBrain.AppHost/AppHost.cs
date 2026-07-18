using Aspire.Hosting;
using Brain.Modules.Ai;
using Brain.Modules.Flutter;
using Brain.Modules.Google;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var journal = storage.AddBlobs("journal");

var brainKernel = builder.AddProject<Projects.Brain_Kernel_Host>("kernel")
    .WithHttpEndpoint(port: 5311, name: "google-oauth")
    .WithDigitalBrainGoogle()
    .WithDigitalBrainAI()
    .WithDigitalBrainFlutter()
    .WithReference(journal)
    .WaitFor(storage);

builder.AddProject<Projects.Brain_Mcp>("brain-mcp").WaitFor(brainKernel);
builder.AddViteApp("brain-docs", "../../website")
    .WithNpm(installCommand: "ci", installArgs: ["--no-audit", "--no-fund"])
    .WithEnvironment("NODE_ENV", "development")
    .WithExternalHttpEndpoints();

builder.Build().Run();
