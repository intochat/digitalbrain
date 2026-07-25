using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain");

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUiEdge()
    .WithFlutterHost());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

var silo = builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);

builder.AddProject<Projects.DigitalBrain_Mcp>("digitalbrain-mcp")
    .WithReference(brain.AsClient())
    .WaitFor(silo)
    .WithEnvironment("DigitalBrain__Owner", "dev")
    .WithHttpEndpoint(port: 5000, name: "http", isProxied: false);

builder.AddViteApp("website", "../../docs")
    .WithExternalHttpEndpoints();

builder.Build().Run();
