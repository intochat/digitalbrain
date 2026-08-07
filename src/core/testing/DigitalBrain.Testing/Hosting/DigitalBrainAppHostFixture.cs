using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace DigitalBrain.Testing;

public abstract class DigitalBrainAppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private RunningAppHost? _active;
    private bool _disposed;
    private int _pendingStarts;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public Task<RunningAppHost> StartAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pendingStarts = checked(_pendingStarts + 1);
        }

        return StartCoreAsync(cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Fixture disposal must tear down any active graph even when stop fails; original cleanup failure is rethrown after best-effort dispose.")]
    public async ValueTask DisposeAsync()
    {
        RunningAppHost? active;
        var pending = 0;
        lock (_sync)
        {
            _disposed = true;
            active = _active;
            _active = null;
            pending = _pendingStarts;
            _pendingStarts = 0;
        }

        GC.SuppressFinalize(this);

        Exception? cleanupFailure = null;
        if (active is not null)
        {
            try
            {
                await active.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                cleanupFailure = failure;
            }
        }

        if (pending != 0 || cleanupFailure is not null)
        {
            throw new InvalidOperationException(
                "The AppHost fixture was disposed with a pending start or active graph handle.",
                cleanupFailure);
        }
    }

    protected abstract Task<DistributedApplication> BuildApplicationAsync(
        CancellationToken cancellationToken);

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Failed startup must preserve its original exception while best-effort cleanup releases the graph and exclusive lease.")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The lease is disposed in the failure path or transferred to RunningAppHost and nulled immediately after successful registration.")]
    private async Task<RunningAppHost> StartCoreAsync(
        CancellationToken cancellationToken)
    {
        AppHostExclusiveLease? lease = null;
        DistributedApplication? application = null;

        try
        {
            lease = await AppHostExclusiveLease.AcquireAsync(cancellationToken).ConfigureAwait(false);
            using var startup = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(StartupTimeout);

            application = await BuildApplicationAsync(startup.Token).ConfigureAwait(false);
            await application.StartAsync(startup.Token).ConfigureAwait(false);

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
                    await application.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (lease is not null)
            {
                try
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            ClearPendingStart();
            throw;
        }
    }

    private void Register(RunningAppHost running)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pendingStarts <= 0)
            {
                throw new InvalidOperationException(
                    "The AppHost fixture has no pending start to register.");
            }

            if (_active is not null)
            {
                throw new InvalidOperationException(
                    "The AppHost fixture already owns an active graph handle.");
            }

            _pendingStarts--;
            _active = running;
        }
    }

    private void ClearPendingStart()
    {
        lock (_sync)
        {
            if (_pendingStarts > 0)
            {
                _pendingStarts--;
            }
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

public abstract class DigitalBrainAppHostFixture<TAppHost> : DigitalBrainAppHostFixture
    where TAppHost : class
{
    private static readonly bool ResourceLoggingEnabled =
        string.Equals(
            Environment.GetEnvironmentVariable("DIGITALBRAIN_APPHOST_RESOURCE_LOGS"),
            "1",
            StringComparison.Ordinal)
        || string.Equals(
            Environment.GetEnvironmentVariable("DIGITALBRAIN_APPHOST_RESOURCE_LOGS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    protected override async Task<DistributedApplication> BuildApplicationAsync(
        CancellationToken cancellationToken)
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<TAppHost>(
                args: [],
                configureBuilder: static (options, _) =>
                    options.EnableResourceLogging = ResourceLoggingEnabled,
                cancellationToken).ConfigureAwait(false);
        return await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
    }
}
