using Microsoft.Extensions.Configuration;

namespace DigitalBrain.ClickHouse;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed class ClickHouseModuleOptions
{
    public const string SectionName = "DigitalBrain:ClickHouse";

    public string? Provider { get; set; }
    public string ConnectionName { get; set; } = ClickHouseRegistration.DefaultConnectionName;
    public string? ConnectionString { get; internal set; }
    public ClickHouseResourceOptions Hosting { get; set; } = new();

    internal void ResolveConnection(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(ConnectionName))
        {
            ConnectionName = ClickHouseRegistration.DefaultConnectionName;
        }

        ConnectionString = configuration.GetConnectionString(ConnectionName);
    }
}

public sealed class ClickHouseResourceOptions
{
    public bool Enabled { get; set; }
    public bool PersistentStorage { get; set; } = true;
    public bool AlwaysRunInitScripts { get; set; }
    public List<string> Seeds { get; set; } = [];
    public ClickHouseResourceOptions WithSeed(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Seeds.Contains(name, StringComparer.Ordinal)) { Seeds.Add(name); }
        return this;
    }
}