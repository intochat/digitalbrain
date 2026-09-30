using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Testing;

public static class TestLogging
{
    public static IReadOnlyDictionary<string, string?> QuietDefaults { get; } = new Dictionary<string, string?>
    {
        ["Logging:LogLevel:Default"] = "Warning",
        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
        ["Logging:LogLevel:Orleans"] = "Warning",
        ["Logging:LogLevel:DigitalBrain"] = "Information",
    };

    public static void Apply(IConfigurationBuilder configuration) => configuration.AddInMemoryCollection(QuietDefaults);
}
