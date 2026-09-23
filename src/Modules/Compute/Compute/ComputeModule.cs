using DigitalBrain.Compute.Ledger;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Orleans.Hosting;

namespace DigitalBrain.Compute;

[ModuleConfiguration(typeof(ComputeConfigurationContract))]
public sealed class ComputeModule : IModule
{
    public const string ConnectionStringKey = "DigitalBrain:Compute:ConnectionString";

    public static ModuleDefinition Define() => new(typeof(ComputeModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        var connectionString = silo.Configuration[ConnectionStringKey];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));
            services.TryAddSingleton<IMeterStore, PostgresMeterStore>();
            services.TryAddSingleton<ILedgerStore, PostgresLedgerStore>();
        }
        else
        {
            services.TryAddSingleton<IMeterStore, InMemoryMeterStore>();
            services.TryAddSingleton<ILedgerStore, InMemoryLedgerStore>();
        }
        services.TryAddSingleton<IMeterSink, DurableMeterSink>();
    }
}
