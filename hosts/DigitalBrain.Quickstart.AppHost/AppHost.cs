using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Quickstart;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("quickstart")
    .AddModule<QuickstartModule>();

builder.AddProject<Projects.DigitalBrain_Quickstart_Host>("host")
    .WithReference(brain);

builder.Build().Run();
