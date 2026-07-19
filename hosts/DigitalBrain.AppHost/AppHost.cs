using DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var journal = storage.AddBlobs("journal");

var openAiKey = builder.AddParameter("openai-key", secret: true);

var brain = builder.AddBrain("brain")
    .WithDevelopmentStores()
    .WithModel(ModelTier.Balanced, ModelProviders.OpenAi, "gpt-5.1", openAiKey);

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain)
    .WithReference(journal);

builder.Build().Run();
