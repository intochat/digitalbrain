namespace DigitalBrain.ClickHouse;

public sealed class ClickHouseModule : Core.IModule
{
    public const string ConfigurationRoot = "DigitalBrain:ClickHouse";
    public const string ProviderConfigurationKey = "DigitalBrain:ClickHouse:Provider";
    public const string DriverProviderName = "ClickHouse";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }
}
