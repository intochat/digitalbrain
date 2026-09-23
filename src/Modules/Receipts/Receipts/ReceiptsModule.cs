using DigitalBrain.Compute;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Receipts;

[ModuleConfiguration(typeof(ReceiptsConfigurationContract))]
public sealed class ReceiptsModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(ReceiptsModule));

    public void Configure(ISiloBuilder silo)
    {
        // The single price book lives in the Compute module; receipts only need to shadow-price
        // usage until P2.2 turns metering into a ledger.
        silo.Services.TryAddSingleton<IPriceBook, PriceBook>();
    }
}
