using System.Diagnostics;

namespace DigitalBrain.Testing;

public sealed record BrowserOptions
{
    public bool Headless { get; init; } = true;
    public float SlowMoMilliseconds { get; init; }

    public static bool HeadedRequested =>
        string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADED"), "1", StringComparison.OrdinalIgnoreCase);

    public static BrowserOptions Default => Resolve(headless: true);

    public static BrowserOptions Resolve(bool headless)
    {
        var headed = !headless || HeadedRequested || Debugger.IsAttached;
        return new()
        {
            Headless = !headed,
            SlowMoMilliseconds = headed ? 250 : 0,
        };
    }
}
