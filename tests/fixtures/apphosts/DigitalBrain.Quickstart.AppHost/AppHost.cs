using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Quickstart;

const string Brain = "quickstart";
const string Host = "host";

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(Brain)
    .AddModule<QuickstartModule>();

builder.AddProject<Projects.DigitalBrain_Quickstart_Host>(Host)
    .WithReference(brain);

builder.Build().Run();
