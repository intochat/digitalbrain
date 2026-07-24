using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainModuleBuilder<TModule>
    where TModule : class, IModule, new()
{
    internal DigitalBrainModuleBuilder(DigitalBrainBuilder brain) => Brain = brain;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public DigitalBrainBuilder Brain { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddProjection(DigitalBrainModuleProjection projection)
        => Brain.AddProjection(projection);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RequireStateProtection()
        => Brain.RequireStateProtection();
}
