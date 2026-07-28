using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainModuleBuilder<TModule>
    where TModule : class, IModule, new()
{
    internal DigitalBrainModuleBuilder(DigitalBrainBuilder brain) => Brain = brain;

    internal DigitalBrainBuilder Brain { get; }

    internal void AddProjection(DigitalBrainModuleProjection projection)
        => Brain.AddProjection(projection);

    internal void ConfigureFeature(string feature)
        => Brain.ConfigureFeature(feature);

    internal void RequireStateProtection()
        => Brain.RequireStateProtection();
}
