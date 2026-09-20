using Microsoft.Extensions.Configuration;

namespace DigitalBrain.ClickHouse;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed class ClickHouseModuleOptions
{
    public const string SectionName = "DigitalBrain:ClickHouse";

    public string? Provider { get; set; }
    public string ConnectionName { get; set; } = ClickHouseRegistration.DefaultConnectionName;
    public string? ConnectionString { get; internal set; }

    internal void ResolveConnection(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(ConnectionName))
        {
            ConnectionName = ClickHouseRegistration.DefaultConnectionName;
        }

        ConnectionString = configuration.GetConnectionString(ConnectionName);
    }
}
