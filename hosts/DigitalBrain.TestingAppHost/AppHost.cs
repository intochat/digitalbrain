using DigitalBrain.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain");

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);

builder.Build().Run();
