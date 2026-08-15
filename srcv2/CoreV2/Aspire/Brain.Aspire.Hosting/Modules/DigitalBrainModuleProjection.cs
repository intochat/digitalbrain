using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public abstract class DigitalBrainModuleProjection
{
    public virtual void ApplyToRuntime<TResource>(IResourceBuilder<TResource> builder)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
    }

    public virtual void ApplyToClient<TResource>(IResourceBuilder<TResource> builder)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
    }
}
