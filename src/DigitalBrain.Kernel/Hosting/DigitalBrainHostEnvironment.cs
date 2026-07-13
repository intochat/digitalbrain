using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Hosting;

public static class DigitalBrainHostEnvironment
{
    private static readonly string[] AspireConnectionKeys =
    [
        "ConnectionStrings__clustering",
        "ConnectionStrings__grainstate",
        "ConnectionStrings__journal"
    ];

    public static bool IsAspireHosted(IConfiguration? configuration = null) =>
        !string.IsNullOrWhiteSpace(configuration?["DigitalBrain:Storage:AccountName"]) ||
        AspireConnectionKeys.Any(static key => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))) ||
        (configuration is not null && new[] { "clustering", "grainstate", "journal" }
            .Any(key => !string.IsNullOrWhiteSpace(configuration.GetConnectionString(key))));
}
