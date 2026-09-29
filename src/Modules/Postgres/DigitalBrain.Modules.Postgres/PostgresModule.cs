using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Postgres;

[ModuleConfiguration(typeof(PostgresConfigurationContract))]
[ModuleHosting("DigitalBrain.Postgres.Aspire.Hosting.PostgresModuleHosting, DigitalBrain.Modules.Postgres.Aspire.Hosting")]
public sealed class PostgresModule : IModule
{
    public const string ConnectionName = "postgres";
    public const string ProviderName = "Npgsql";

    public static ModuleDefinition Define() => Define(new PostgresModuleOptions());

    public static ModuleDefinition Define(PostgresModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionName);
        ArgumentNullException.ThrowIfNull(options.Hosting);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Hosting.DatabaseName);
        return new(typeof(PostgresModule), new Dictionary<string, string?>
        {
            [PostgresModuleOptions.SectionName + ":ConnectionName"] = options.ConnectionName,
            [PostgresModuleOptions.SectionName + ":Hosting:Enabled"] = options.Hosting.Enabled.ToString(),
            [PostgresModuleOptions.SectionName + ":Hosting:DatabaseName"] = options.Hosting.DatabaseName,
            [PostgresModuleOptions.SectionName + ":Hosting:PersistentStorage"] = options.Hosting.PersistentStorage.ToString(),
        });
    }

    public void Configure(ISiloBuilder silo) => silo.AddPostgres();
}
