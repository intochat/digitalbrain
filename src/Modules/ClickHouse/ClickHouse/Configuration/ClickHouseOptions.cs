using Microsoft.Extensions.Configuration;

namespace DigitalBrain.ClickHouse;

public sealed class ClickHouseOptions
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
