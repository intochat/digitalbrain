namespace DigitalBrain.Substrate.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    internal void Advance(TimeSpan elapsed) => _utcNow += elapsed;
}
