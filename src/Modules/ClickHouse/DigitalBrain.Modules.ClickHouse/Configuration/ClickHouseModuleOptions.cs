using System.Text.Json.Serialization;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.ClickHouse;

public sealed class ClickHouseModuleOptions : IModuleOptions
{
    public string? Provider { get; set; }
    public string ConnectionName { get; set; } = ClickHouseRegistration.DefaultConnectionName;
    [JsonIgnore]
    public string? ConnectionString { get; internal set; }
    public ClickHouseResourceOptions Hosting { get; set; } = new();

    public ClickHouseModuleOptions WithClickHouse(Action<ClickHouseResourceOptions>? configure = null)
    {
        Provider = ClickHouseModule.DriverProviderName;
        Hosting.Enabled = true;
        configure?.Invoke(Hosting);
        return this;
    }

    public void Validate()
    {
        if (Hosting.Seeds.Any(string.IsNullOrWhiteSpace))
        { throw new ArgumentException("ClickHouse seed names must not be blank."); }
    }

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
