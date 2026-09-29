using DigitalBrain.Core;

namespace DigitalBrain.Postgres;

/// <summary>Public settings. Connection credentials remain in the host's private configuration.</summary>
public sealed class PostgresModuleOptions
{
    public const string SectionName = "DigitalBrain:Postgres";
    public string ConnectionName { get; set; } = PostgresModule.ConnectionName;
    public PostgresResourceOptions Hosting { get; set; } = new();
}

public sealed class PostgresResourceOptions
{
    public bool Enabled { get; set; }
    public string DatabaseName { get; set; } = "postgres";
    public bool PersistentStorage { get; set; } = true;
}

public sealed class PostgresConfigurationContract() : ModuleConfigurationContract<PostgresModule, PostgresModuleOptions>(
    "ConnectionName", "Hosting.Enabled", "Hosting.DatabaseName", "Hosting.PersistentStorage")
{
    protected override ModuleDefinition Compile(PostgresModuleOptions options) => PostgresModule.Define(options);
}

public static class PostgresModuleConfiguration
{
    public static ModuleConfiguration<PostgresModule> WithConnection(this ModuleConfiguration<PostgresModule> module, string name)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        module.ConfigureOptions<PostgresModuleOptions>(options =>
        {
            options.ConnectionName = name;
            options.Hosting.Enabled = false;
        }, "ConnectionName", "Hosting.Enabled");
        return module;
    }

    public static ModuleConfiguration<PostgresModule> WithPostgres(this ModuleConfiguration<PostgresModule> module,
        Action<PostgresResourceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        module.ConfigureOptions<PostgresModuleOptions>(options =>
        {
            options.Hosting.Enabled = true;
            configure?.Invoke(options.Hosting);
        }, "Hosting.Enabled", "Hosting.DatabaseName", "Hosting.PersistentStorage");
        return module;
    }
}
