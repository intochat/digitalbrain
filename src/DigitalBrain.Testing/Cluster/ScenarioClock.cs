namespace DigitalBrain.Testing;

public sealed class ScenarioClock : TimeProvider
{
    private readonly object _gate = new();
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return DateTimeOffset.UtcNow + _offset;
        }
    }

    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        lock (_gate)
        {
            _offset += delta;
        }
    }
}
