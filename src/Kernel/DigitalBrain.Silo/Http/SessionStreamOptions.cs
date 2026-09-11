namespace DigitalBrain.Kernel;

internal sealed class SessionStreamOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}
