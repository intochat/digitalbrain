using DigitalBrain.Aspire.Hosting;

const string Silo = "silo";

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain");

builder.AddProject<Projects.DigitalBrain_Host>(Silo)
    .WithReference(brain);

builder.Build().Run();
