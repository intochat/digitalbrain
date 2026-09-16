using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainModuleBuilder<TModule>
    where TModule : class
{
    internal DigitalBrainModuleBuilder(DigitalBrainBuilder brain) => Brain = brain;

    public DigitalBrainBuilder Brain { get; }
    public IResourceBuilder<DigitalBrainModuleResource> Resource => Brain.GetModuleResource<TModule>();

    public void AddProjection(DigitalBrainModuleProjection projection)
        => Brain.AddProjection(projection);
}
