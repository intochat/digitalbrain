using Aspire.Hosting;

namespace DigitalBrain.Testing;

public sealed class RunningAppHost : IAsyncDisposable
{
    private readonly DistributedApplication _application;
    private readonly AppHostExclusiveLease _lease;
    private readonly Action<RunningAppHost> _release;
    private int _disposed;

    internal RunningAppHost(
        DistributedApplication application,
        AppHostExclusiveLease lease,
        Action<RunningAppHost> release)
    {
        _application = application;
        _lease = lease;
        _release = release;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        GC.SuppressFinalize(this);

        try
        {
            try
            {
                await _application.StopAsync(CancellationToken.None);
            }
            finally
            {
                await _application.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                _release(this);
            }
            finally
            {
                await _lease.DisposeAsync();
            }
        }
    }
}
