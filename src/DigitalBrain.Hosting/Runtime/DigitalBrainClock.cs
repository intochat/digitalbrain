namespace DigitalBrain;

internal sealed class DigitalBrainClock
{
    private readonly TimeProvider timeProvider;

    internal DigitalBrainClock(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
