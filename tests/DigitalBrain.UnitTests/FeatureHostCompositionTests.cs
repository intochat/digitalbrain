using System.Reflection;
using Azure.Storage.Blobs;
using DigitalBrain.FeatureHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class FeatureHostCompositionTests
{
    [Fact]
    public void Production_composition_resolves_one_worker_and_every_runtime_dependency()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["DigitalBrain:FeatureHost:InternalOrigin"] = "https://kernel.internal";
        builder.Configuration["DigitalBrain:FeatureHost:InternalToken"] = new string('x', 32);
        builder.Services.AddKeyedSingleton<BlobServiceClient>("features", new BlobServiceClient(
            "DefaultEndpointsProtocol=http;AccountName=test;AccountKey=" +
            Convert.ToBase64String(new byte[32]) +
            ";BlobEndpoint=http://127.0.0.1:10000/test;"));
        builder.Services.AddSingleton(DispatchProxy.Create<IClusterClient, ThrowingProxy>());

        builder.Services.AddDigitalBrainFeatureHost(builder.Configuration);
        using var host = builder.Build();

        var hostedWorkers = host.Services.GetServices<IHostedService>()
            .OfType<FeatureExecutionWorker>()
            .ToArray();
        Assert.Single(hostedWorkers);
        Assert.Same(hostedWorkers[0], host.Services.GetRequiredService<FeatureExecutionWorker>());
        Assert.IsType<BlobFeatureArtifactCatalog>(host.Services.GetRequiredService<IFeatureArtifactCatalog>());
        Assert.IsType<OrleansFeatureWorkSource>(host.Services.GetRequiredService<IFeatureWorkSource>());
        Assert.IsType<CapabilityFeatureRunContextFactory>(host.Services.GetRequiredService<IFeatureRunContextFactory>());
        Assert.NotNull(host.Services.GetRequiredService<FeatureReleaseManager>());
        Assert.NotNull(host.Services.GetRequiredService<FeatureExecutionOptions>());
        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
    }

    private class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException();
    }
}
