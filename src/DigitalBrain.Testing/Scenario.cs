using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class Scenario : IAsyncDisposable
{
    private int _disposed;

    internal Scenario(OwnerId owner, TimeProvider clock, IGrainFactory grains)
    {
        Owner = owner;
        Clock = clock;
        Grains = grains;
    }

    public OwnerId Owner { get; }

    public TimeProvider Clock { get; }

    public IGrainFactory Grains { get; }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}
