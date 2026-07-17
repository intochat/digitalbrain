using Aspire.Hosting;
using Brain.Modules.Ai;
using Brain.Modules.Google;

var builder = DistributedApplication.CreateBuilder(args);

var brainKernel = builder.AddProject<Projects.Brain_Kernel_Host>("brain-kernel")
    .WithHttpEndpoint(port: 5311, name: "google-oauth")
    .WithDigitalBrainGoogle()
    .WithDigitalBrainAI();

builder.AddProject<Projects.Brain_Mcp>("brain-mcp").WaitFor(brainKernel);
builder.AddProject<Projects.Brain_UiGateway>("brain-ui").WithHttpEndpoint(port: 5320).WaitFor(brainKernel);
builder.AddViteApp("brain-docs", "../../website")
    .WithNpm(installCommand: "ci", installArgs: ["--no-audit", "--no-fund"])
    .WithEnvironment("NODE_ENV", "development")
    .WithExternalHttpEndpoints();

builder.Build().Run();
