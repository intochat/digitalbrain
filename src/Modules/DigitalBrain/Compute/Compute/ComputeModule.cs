using DigitalBrain.Compute.Allowances;
using DigitalBrain.Compute.Billing;
using DigitalBrain.Compute.Ledger;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Orleans.Hosting;

namespace DigitalBrain.Compute;

[ModuleConfiguration(typeof(ComputeConfigurationContract))]
public sealed class ComputeModule : IModule
{
    public const string ConnectionStringKey = "DigitalBrain:Compute:ConnectionString";
    public const string LedgerConnectionName = "compute";

    public static ModuleDefinition Define() => new(typeof(ComputeModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        var connectionString = silo.Configuration[ConnectionStringKey]
            ?? silo.Configuration.GetConnectionString(LedgerConnectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.TryAddSingleton(_ => new ComputeDatabase(NpgsqlDataSource.Create(connectionString)));
            services.TryAddSingleton<IMeterStore, PostgresMeterStore>();
            services.TryAddSingleton<ILedgerStore, PostgresLedgerStore>();
        }
        else
        {
            services.TryAddSingleton<IMeterStore, InMemoryMeterStore>();
            services.TryAddSingleton<ILedgerStore, InMemoryLedgerStore>();
        }
        services.TryAddSingleton<IMeterSink, DurableMeterSink>();
        services.TryAddSingleton<IBatchMeterSink>(provider => (IBatchMeterSink)provider.GetRequiredService<IMeterSink>());
        services.AddHostedService<ComputeSchemaWarmer>();
        services.TryAddSingleton<IPriceBook, PriceBook>();
        services.TryAddSingleton<IInvoicingProvider, FakeInvoicingProvider>();
        services.TryAddSingleton<IAllowancePolicySource, GrainAllowancePolicySource>();
        // The allowance stage is an increment of the one call filter. It runs before grants (Order -1)
        // so a granted app call without an allowance is still stopped.
        services.AddSingleton<ICallFilterStage, AllowanceCallFilterStage>();
    }
}
