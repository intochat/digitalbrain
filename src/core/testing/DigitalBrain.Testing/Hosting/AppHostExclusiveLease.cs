namespace DigitalBrain.Testing;

internal sealed class AppHostExclusiveLease : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private int _disposed;

    private AppHostExclusiveLease()
    {
    }

    internal static async Task<AppHostExclusiveLease> AcquireAsync(
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        return new AppHostExclusiveLease();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Gate.Release();
        }

        return ValueTask.CompletedTask;
    }
}
