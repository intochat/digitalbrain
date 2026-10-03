using DigitalBrain.Kernel;
using Orleans.Hosting;

namespace DigitalBrain.Postgres;

public sealed class PostgresModule : IModule<PostgresModuleOptions>
{
    public const string ConnectionName = "postgres";
    public const string ProviderName = "Npgsql";

    public void Configure(ISiloBuilder silo) => silo.AddPostgres();
}
