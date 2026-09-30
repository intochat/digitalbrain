using DigitalBrain.Compute.Allowances;
using DigitalBrain.Compute.Billing;
using DigitalBrain.Compute.Ledger;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Compute.Storage;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Compute;

public sealed class ComputeModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton<IMeterStore, NeuronMeterStore>();
        services.TryAddSingleton<ILedgerStore, NeuronLedgerStore>();
        services.TryAddSingleton<IUsageStore, NeuronUsageStore>();
        services.TryAddSingleton<IMeterSink, DurableMeterSink>();
        services.TryAddSingleton<IBatchMeterSink>(provider => (IBatchMeterSink)provider.GetRequiredService<IMeterSink>());
        services.TryAddSingleton<IPriceBook, PriceBook>();
        services.TryAddSingleton<IInvoicingProvider, FakeInvoicingProvider>();
        services.TryAddSingleton<IAllowancePolicySource, GrainAllowancePolicySource>();
        // The allowance stage is an increment of the one call filter. It runs before grants (Order -1)
        // so a granted app call without an allowance is still stopped.
        services.AddSingleton<ICallFilterStage, AllowanceCallFilterStage>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ComputeEndpoints.Map(endpoints);
    }
}
