using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Receipts;

[ModuleConfiguration(typeof(ReceiptsConfigurationContract))]
public sealed class ReceiptsModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(ReceiptsModule));

    public void Configure(ISiloBuilder silo) => ArgumentNullException.ThrowIfNull(silo);
}
