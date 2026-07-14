using DigitalBrain.FeatureHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

var builder = Host.CreateApplicationBuilder(args);
var clusteringServiceKey = builder.Configuration["Orleans:Clustering:ServiceKey"] ?? "clustering";
builder.AddKeyedAzureTableServiceClient(clusteringServiceKey);
builder.AddKeyedAzureBlobServiceClient("features");
builder.UseOrleansClient();
builder.Services.AddDigitalBrainFeatureHost(builder.Configuration);
await builder.Build().RunAsync();
