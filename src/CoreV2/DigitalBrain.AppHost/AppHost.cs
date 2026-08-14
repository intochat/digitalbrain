using Brain.Modules.UI;
using Brain.Modules.UI.Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductResources.Brain);
brain.AddModule<UiModule>(ui => ui.WithWindowHost());
var runtime = builder
    .AddProject<Projects.DigitalBrain_RuntimeHost>(ProductResources.Runtime)
    .WithReference(brain)
    .WithHttpEndpoint(name: ProductResources.HttpEndpoint)
    .WithHttpHealthCheck("/health", endpointName: ProductResources.HttpEndpoint);

builder
    .AddProject<Projects.DigitalBrain_ProductHost>(ProductResources.Product)
    .WithReference(brain.AsClient())
    .WithHttpEndpoint(name: ProductResources.HttpEndpoint)
    .WithHttpHealthCheck("/health", endpointName: ProductResources.HttpEndpoint)
    .WaitFor(runtime);

builder.Build().Run();
