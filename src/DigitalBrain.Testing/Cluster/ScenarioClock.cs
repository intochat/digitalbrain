namespace DigitalBrain.Testing;

public sealed class ScenarioClock : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _utcNow;

    public ScenarioClock(DateTimeOffset? startUtc = null)
    {
        _utcNow = startUtc ?? DateTimeOffset.UtcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_gate)
        {
            _utcNow += delta;
        }
    }
}
