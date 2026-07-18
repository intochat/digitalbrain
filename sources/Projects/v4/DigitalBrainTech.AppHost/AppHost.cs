using Aspire.Hosting;
using DigitalBrain.Abstractions.Bundles;

var builder = DistributedApplication.CreateBuilder(args);

// Demo wire to new split (per executed plan): use abstraction from core (thin contracts for future separate repo)
_ = new BundleId("demo-from-new-structure");

var cache = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects.DigitalBrainTech_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.DigitalBrainTech_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
