namespace DigitalBrain.Testing;

public sealed class FaultHandle : IAsyncDisposable
{
    private readonly Action _disarm;
    private int _disposed;

    internal FaultHandle(Action disarm)
        => _disarm = disarm;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _disarm();
        }

        return ValueTask.CompletedTask;
    }
}
