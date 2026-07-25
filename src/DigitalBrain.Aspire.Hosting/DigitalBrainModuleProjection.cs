using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

internal abstract class DigitalBrainModuleProjection
{
    public abstract void Apply<TResource>(IResourceBuilder<TResource> builder)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints;
}
