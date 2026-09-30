using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Postgres.Aspire.Hosting;

public sealed class PostgresModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var options = brain.GetModuleConfiguration<PostgresModule>().GetModuleOptions<PostgresModuleOptions>(nameof(PostgresModule));
        var module = new DigitalBrainModuleBuilder<PostgresModule>(brain);
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
