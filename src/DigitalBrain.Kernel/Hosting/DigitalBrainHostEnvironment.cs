using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Hosting;

public static class DigitalBrainHostEnvironment
{
    private static readonly string[] AspireConnectionKeys =
    [
        "ConnectionStrings__clustering",
        "ConnectionStrings__grainstate"
    ];

    public static bool IsAspireHosted(IConfiguration? configuration = null) =>
        !string.IsNullOrWhiteSpace(configuration?["DigitalBrain:Storage:AccountName"]) ||
        AspireConnectionKeys.Any(static key => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))) ||
        (configuration is not null && new[] { "clustering", "grainstate" }
            .Any(key => !string.IsNullOrWhiteSpace(configuration.GetConnectionString(key))));
}
