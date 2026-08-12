using DigitalBrain.Core;

namespace DigitalBrain.Kinds;

public sealed class KindsModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        CellKindCatalog.Register(CalculatorKind.Instance);
    }
}
