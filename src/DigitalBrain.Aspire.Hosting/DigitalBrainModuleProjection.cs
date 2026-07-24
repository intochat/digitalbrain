using System.ComponentModel;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class DigitalBrainModuleProjection
{
    public abstract void Apply<TResource>(IResourceBuilder<TResource> builder)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints;
}
