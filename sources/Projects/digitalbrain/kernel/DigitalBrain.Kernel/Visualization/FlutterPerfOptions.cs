namespace DigitalBrain.Kernel.Visualization;

public sealed class FlutterPerfOptions
{
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan WindowSeconds { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(8);
    public double P95SmoothMs   { get; set; } = 16.0;
    public double P95StrainedMs { get; set; } = 33.0;
    public TimeSpan TierCrossingDebounce { get; set; } = TimeSpan.FromSeconds(1);
}
