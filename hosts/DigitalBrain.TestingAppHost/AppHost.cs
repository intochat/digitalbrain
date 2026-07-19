using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var journal = storage.AddBlobs("journal");

var syntheticKey = builder.AddParameter("openai-key", "synthetic-key", secret: true);

var brain = builder.AddBrain("brain")
    .WithDevelopmentStores()
    .WithModel(ModelTier.Balanced, ModelProviders.OpenAi, "probe-model", syntheticKey);

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain)
    .WithReference(journal)
    .WaitFor(journal);

builder.AddProject<Projects.DigitalBrain_ProbeHost>("probe")
    .WithReference(brain)
    .WithReference(journal)
    .WaitFor(journal);

builder.Build().Run();
