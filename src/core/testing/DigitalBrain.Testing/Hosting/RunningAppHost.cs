using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

public sealed class RunningAppHost : IAsyncDisposable
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(5);

    private readonly DistributedApplication _application;
    private readonly TimeSpan _cleanupTimeout;
    private readonly AppHostExclusiveLease _lease;
    private readonly Action<RunningAppHost> _release;
    private readonly HashSet<string> _resourceNames;
    private readonly Dictionary<string, HostedResource> _resources =
        new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private int _disposed;

    internal RunningAppHost(
        DistributedApplication application,
        AppHostExclusiveLease lease,
        Action<RunningAppHost> release,
        TimeSpan? cleanupTimeout = null)
    {
        _application = application;
        _lease = lease;
        _release = release;
        _cleanupTimeout = cleanupTimeout ?? CleanupTimeout;

        _resourceNames = application.Services
            .GetRequiredService<DistributedApplicationModel>()
            .Resources
            .Select(resource => resource.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    public HostedResource Resource(string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_resourceNames.Contains(name))
            {
                var known = string.Join(
                    ", ",
                    _resourceNames.Order(StringComparer.Ordinal));
                throw new AppHostTestFailureException(
                    $"Resource '{name}' does not exist. Known resources: {known}.");
            }

            if (!_resources.TryGetValue(name, out var resource))
            {
                resource = new HostedResource(this, name);
                _resources.Add(name, resource);
            }

            return resource;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        GC.SuppressFinalize(this);

        using var cleanup = new CancellationTokenSource(_cleanupTimeout);
        var failures = new List<Exception>();

        await AttemptAsync(
            () => StopRuntimeResourcesAsync(cleanup.Token),
            failures,
            cleanup.Token);
        await AttemptAsync(
            () => _application.StopAsync(cleanup.Token),
            failures,
            cleanup.Token);
        await AttemptAsync(
            () => _application.DisposeAsync().AsTask(),
            failures,
            cleanup.Token);
        Attempt(_ => _release(this), failures, cleanup.Token);
        await AttemptAsync(
            () => _lease.DisposeAsync().AsTask(),
            failures,
            cleanup.Token);

        ReportFailures(failures);
    }

    internal HttpClient CreateHttpClient(string resourceName)
    {
        ThrowIfDisposed();
        return _application.CreateHttpClient(resourceName);
    }

    internal async Task WaitUntilHealthyAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operation = Linked(cancellationToken);
        await _application.ResourceNotifications.WaitForResourceHealthyAsync(
            resourceName,
            operation.Token);
    }

    private static CancellationTokenSource Linked(
        CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        linked.CancelAfter(OperationTimeout);
        return linked;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Graph cleanup attempts every terminal stage and preserves all failures.")]
    private static async Task AttemptAsync(
        Func<Task> operation,
        List<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation().WaitAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Graph cleanup attempts every terminal stage and preserves all failures.")]
    private static void Attempt(
        Action<CancellationToken> operation,
        List<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            operation(cancellationToken);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    private static void ReportFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        var primary = failures[0];
        if (failures.Count > 1)
        {
            primary.Data["AppHost cleanup secondary failures"] =
                new AggregateException(failures.Skip(1));
        }

        throw new AppHostTestFailureException(
            "AppHost graph cleanup failed.",
            primary);
    }

    private async Task StopRuntimeResourcesAsync(
        CancellationToken cancellationToken)
    {
        var stoppable = _resourceNames
            .Where(id => !IsTerminal(id) && AdvertisesStop(id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Task.WhenAll(
            stoppable.Select(id => StopOneAsync(id, cancellationToken)));
    }

    private bool AdvertisesStop(string resourceId)
        => _application.ResourceNotifications.TryGetCurrentState(
                resourceId,
                out var resourceEvent)
            && resourceEvent is not null
            && resourceEvent.Snapshot.Commands.Any(command =>
                string.Equals(
                    command.Name,
                    KnownResourceCommands.StopCommand,
                    StringComparison.Ordinal));

    private bool IsTerminal(string resourceId)
        => _application.ResourceNotifications.TryGetCurrentState(
                resourceId,
                out var resourceEvent)
            && resourceEvent?.Snapshot.State?.Text is { } state
            && KnownResourceStates.TerminalStates.Contains(
                state,
                StringComparer.Ordinal);

    private async Task StopOneAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        var result = await _application.ResourceCommands.ExecuteCommandAsync(
            resourceId,
            KnownResourceCommands.StopCommand,
            cancellationToken);

#pragma warning disable CS0618
        var errorMessage = result.ErrorMessage;
#pragma warning restore CS0618

        if (!result.Success && !IsTerminal(resourceId))
        {
            throw new InvalidOperationException(
                $"Stop failed for '{resourceId}': {errorMessage ?? result.Message}");
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
}
