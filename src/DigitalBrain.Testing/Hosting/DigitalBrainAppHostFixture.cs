using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace DigitalBrain.Testing;

public abstract class DigitalBrainAppHostFixture<TAppHost> : IAsyncLifetime
    where TAppHost : class
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private RunningAppHost? _active;
    private bool _disposed;
    private bool _starting;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public Task<RunningAppHost> StartAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_starting || _active is not null)
            {
                throw new InvalidOperationException(
                    "This AppHost fixture already owns an active graph handle.");
            }

            _starting = true;
        }

        return StartCoreAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            if (_starting || _active is not null)
            {
                return ValueTask.FromException(
                    new InvalidOperationException(
                        "The AppHost fixture was disposed with an active graph handle."));
            }
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Failed startup must preserve its original exception while best-effort cleanup releases the graph and exclusive lease.")]
    private async Task<RunningAppHost> StartCoreAsync(
        CancellationToken cancellationToken)
    {
        AppHostExclusiveLease? lease = null;
        DistributedApplication? application = null;

        try
        {
            lease = await AppHostExclusiveLease.AcquireAsync(cancellationToken);
            var builder = await DistributedApplicationTestingBuilder
                .CreateAsync<TAppHost>(
                    args: [],
                    configureBuilder: static (options, _) =>
                        options.EnableResourceLogging = true,
                    cancellationToken);

            using var startup = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(StartupTimeout);

            application = await builder.BuildAsync(startup.Token);
            await application.StartAsync(startup.Token);

            var running = new RunningAppHost(
                application,
                lease,
                Release);
            Register(running);
            application = null;
            lease = null;
            return running;
        }
        catch
        {
            if (application is not null)
            {
                try
                {
                    await application.DisposeAsync();
                }
                catch
                {
                    // Preserve the startup failure.
                }
            }

            if (lease is not null)
            {
                try
                {
                    await lease.DisposeAsync();
                }
                catch
                {
                    // Preserve the startup failure.
                }
            }

            ClearStartClaim();
            throw;
        }
    }

    private void Register(RunningAppHost running)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _starting = false;
            _active = running;
        }
    }

    private void ClearStartClaim()
    {
        lock (_sync)
        {
            _starting = false;
        }
    }

    private void Release(RunningAppHost running)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_active, running))
            {
                _active = null;
            }
        }
    }
}
