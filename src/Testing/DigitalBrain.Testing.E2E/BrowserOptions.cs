namespace DigitalBrain.Testing.E2E;

public sealed record BrowserOptions
{
    public bool? Headless { get; init; }
    public float? SlowMoMilliseconds { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan AssertionTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
