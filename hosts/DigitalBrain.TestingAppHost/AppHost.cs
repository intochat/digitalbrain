using DigitalBrain.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain");
var probeBrain = builder.AddDigitalBrain("probe");

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);

builder.AddProject<Projects.DigitalBrain_ProbeHost>("probe")
    .WithReference(probeBrain);

builder.Build().Run();
