using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class Scenario : IAsyncDisposable
{
    private readonly ScenarioFaults _faults = new();
    private int _disposed;

    internal Scenario(OwnerId owner, ScenarioClock clock, IGrainFactory grains)
    {
        Owner = owner;
        Clock = clock;
        Grains = grains;
    }

    public OwnerId Owner { get; }

    public TimeProvider Clock { get; }

    public IGrainFactory Grains { get; }

    public void AdvanceClock(TimeSpan delta)
        => ((ScenarioClock)Clock).Advance(delta);

    public FaultHandle Arm(FaultPoint point)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _faults.Arm(point);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _faults.DisarmLeftoversAndThrowIfAnyAsync();
    }
}
