using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var journal = storage.AddBlobs("journal");

var brain = builder.AddBrain("brain")
    .WithDevelopmentStores();

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain)
    .WithReference(journal);

builder.Build().Run();
