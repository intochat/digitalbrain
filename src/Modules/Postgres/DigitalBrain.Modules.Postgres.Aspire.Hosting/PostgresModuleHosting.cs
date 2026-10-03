using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Postgres.Aspire.Hosting;

public sealed class PostgresModuleHosting : IDigitalBrainModuleHosting
{
    public string Id => "postgres";
    public void Configure(DigitalBrainBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var options = brain.GetModuleConfiguration("postgres").GetModuleOptions<PostgresModuleOptions>("postgres");
        var module = new DigitalBrainModuleBuilder<DigitalBrain.Postgres.Aspire.Hosting.PostgresModuleHosting>(brain);
        if (options.Hosting.Enabled)
        {
            module.WithPostgres(hosting =>
            {
                hosting.ConnectionName = options.ConnectionName;
                hosting.DatabaseName = options.Hosting.DatabaseName;
                hosting.PersistentStorage = options.Hosting.PersistentStorage;
            });
        }
        else
        {
            module.WithExternalConnection(options.ConnectionName);
        }
    }
}
