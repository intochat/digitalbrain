using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainModuleBuilder<TModule>
    where TModule : class
{
    public DigitalBrainModuleBuilder(DigitalBrainBuilder digitalBrainBuilder) => DigitalBrainBuilder = digitalBrainBuilder;

    public DigitalBrainBuilder DigitalBrainBuilder { get; }

    public IResourceBuilder<DigitalBrainModuleResource> Resource
        => DigitalBrainBuilder.GetOrAddModuleNode(typeof(TModule));

    public void AddProjection(DigitalBrainModuleProjection projection)
        => DigitalBrainBuilder.AddProjection(projection);
}
