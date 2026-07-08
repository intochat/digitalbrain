namespace DigitalBrain.Kernel.Hosting;

internal static class DigitalBrainHostEnvironment
{
    private static readonly string[] AspireConnectionKeys =
    [
        "ConnectionStrings__clustering",
        "ConnectionStrings__grainstate",
        "ConnectionStrings__journal"
    ];

    public static bool IsAspireHosted() =>
        AspireConnectionKeys.Any(static key => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)));
}
