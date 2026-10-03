using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Qdrant.Aspire.Hosting;

public sealed class QdrantModuleHosting : IDigitalBrainModuleHosting
{
    public string Id => "qdrant";
    public void Configure(DigitalBrainBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var options = brain.GetModuleConfiguration("qdrant").GetModuleOptions<QdrantModuleOptions>("qdrant");
        if (!options.Host) { return; }
        var module = new DigitalBrainModuleBuilder<DigitalBrain.Qdrant.Aspire.Hosting.QdrantModuleHosting>(brain);
        var qdrant = brain.ApplicationBuilder.AddQdrant(brain.ResourceName("qdrant-server"))
            .WithParentRelationship(module.Resource)
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent);
        module.AddProjection(new QdrantProjection(qdrant));
    }

    private sealed class QdrantProjection(IResourceBuilder<QdrantServerResource> qdrant) : DigitalBrainModuleProjection
    {
        // WaitAnnotation: Apply targets IResourceWithEnvironment (kernel), not always IResourceWithWaitSupport.
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .WithReference(qdrant, connectionName: "qdrant")
                .WithAnnotation(new WaitAnnotation(qdrant.Resource, WaitType.WaitUntilHealthy, exitCode: 0));
        }
    }
}
