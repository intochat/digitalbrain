using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainModuleBuilder<TModule>
    where TModule : IDigitalBrainModuleHosting, new()
{
    public DigitalBrainModuleBuilder(DigitalBrainBuilder digitalBrainBuilder) => DigitalBrainBuilder = digitalBrainBuilder;

    public DigitalBrainBuilder DigitalBrainBuilder { get; }

    public IResourceBuilder<DigitalBrainModuleResource> Resource
        => DigitalBrainBuilder.GetOrAddModuleNode(new TModule().Id);

    public void AddProjection(DigitalBrainModuleProjection projection)
        => DigitalBrainBuilder.AddProjection(projection);
}
