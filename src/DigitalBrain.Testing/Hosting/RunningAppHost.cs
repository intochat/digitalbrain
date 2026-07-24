using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "ResourceLoggerService is resolved from and owned by the DistributedApplication service provider, which is disposed by this graph owner.")]
public sealed class RunningAppHost : IAsyncDisposable
{
    private const string RestartCommand = "resource-restart";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(5);
    private readonly DistributedApplication _application;
    private readonly CancellationTokenSource _collectorCancellation = new();
    private readonly object _collectorSync = new();
    private readonly AppHostTestDiagnostics _diagnostics;
    private readonly AppHostExclusiveLease _lease;
    private readonly Dictionary<string, Task> _logCollectors =
        new(StringComparer.Ordinal);
    private readonly Task _notificationCollector;
    private readonly Action<RunningAppHost> _release;
    private readonly ResourceLoggerService? _resourceLogger;
    private readonly Dictionary<string, HostedResource> _resources =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _resourceNames;
    private readonly string _resourceNamesDisplay;
    private readonly object _sync = new();
    private int _disposed;

    internal RunningAppHost(
        DistributedApplication application,
        AppHostExclusiveLease lease,
        Action<RunningAppHost> release)
    {
        _application = application;
        _lease = lease;
        _release = release;

        var model = application.Services
            .GetRequiredService<DistributedApplicationModel>();
        var resources = model.Resources
            .OrderBy(resource => resource.Name, StringComparer.Ordinal)
            .ToArray();
        var resourceNames = resources
            .Select(resource => resource.Name)
            .ToArray();
        _resourceNames = resourceNames.ToHashSet(StringComparer.Ordinal);
        _resourceNamesDisplay = string.Join(", ", resourceNames);
        _diagnostics = new AppHostTestDiagnostics(
            resources.Select(resource => (
                resource.Name,
                resource.GetType().Name)));
        _resourceLogger = application.Services
            .GetService<ResourceLoggerService>();
        _notificationCollector = CollectNotificationsAsync(
            _collectorCancellation.Token);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every validation/runtime failure must be closed over a bounded AppHost artifact while preserving its original exception.")]
    public HostedResource Resource(string name)
    {
        try
        {
            ThrowIfDisposed();
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            HostedResource resource;
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_resourceNames.Contains(name))
                {
                    throw new InvalidOperationException(
                        $"Resource '{name}' does not exist. Known resources: {_resourceNamesDisplay}.");
                }

                if (!_resources.TryGetValue(name, out resource!))
                {
                    resource = new HostedResource(this, name);
                    _resources.Add(name, resource);
                }
            }

            EnsureLogCollector(name);
            return resource;
        }
        catch (AppHostTestFailureException)
        {
            throw;
        }
        catch (Exception failure)
        {
            throw _diagnostics.CaptureFailure(
                "resource.bind",
                name,
                failure);
        }
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
            await DisposeCoreAsync();
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

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every HTTP client creation failure must be closed over a bounded AppHost artifact while preserving its original exception.")]
    internal HttpClient CreateHttpClient(
        string resourceName,
        string? endpointName)
    {
        try
        {
            ThrowIfDisposed();
            return _application.CreateHttpClient(
                resourceName,
                endpointName);
        }
        catch (AppHostTestFailureException)
        {
            throw;
        }
        catch (Exception failure)
        {
            throw _diagnostics.CaptureFailure(
                "resource.http-client",
                resourceName,
                failure);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every health wait failure must be closed over a bounded AppHost artifact while preserving its original exception.")]
    internal async Task WaitUntilHealthyAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        try
        {
            ThrowIfDisposed();
            using var operation = CreateOperationToken(
                cancellationToken);
            await WaitUntilHealthyCoreAsync(
                resourceName,
                operation.Token);
        }
        catch (AppHostTestFailureException)
        {
            throw;
        }
        catch (Exception failure)
        {
            throw _diagnostics.CaptureFailure(
                "resource.wait-healthy",
                resourceName,
                failure);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every restart failure must be closed over a bounded AppHost artifact while preserving its original exception.")]
    internal async Task RestartAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        try
        {
            ThrowIfDisposed();
            using var operation = CreateOperationToken(
                cancellationToken);
            _diagnostics.RecordCommand(
                resourceName,
                RestartCommand,
                "requested",
                detail: null);

            ExecuteCommandResult result;
            try
            {
                result = await _application.ResourceCommands
                    .ExecuteCommandAsync(
                        resourceName,
                        RestartCommand,
                        operation.Token);
            }
            catch (Exception failure)
            {
                _diagnostics.RecordCommand(
                    resourceName,
                    RestartCommand,
                    "failed",
                    $"{failure.GetType().FullName}: {failure.Message}");
                throw;
            }

#pragma warning disable CS0618 // Required failure evidence from the pinned Aspire 13.4.6 result.
            var errorMessage = result.ErrorMessage;
#pragma warning restore CS0618
            _diagnostics.RecordCommand(
                resourceName,
                RestartCommand,
                result.Success ? "succeeded" : "failed",
                $"ErrorMessage='{errorMessage ?? "(null)"}', "
                + $"Message='{result.Message ?? "(null)"}', "
                + $"Canceled={result.Canceled}");

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Restart failed for resource '{resourceName}': "
                    + $"ErrorMessage='{errorMessage ?? "(null)"}', "
                    + $"Message='{result.Message ?? "(null)"}', "
                    + $"Canceled={result.Canceled}, "
                    + $"CurrentState={DescribeResourceState(resourceName)}.");
            }

            await WaitUntilHealthyCoreAsync(
                resourceName,
                operation.Token);
        }
        catch (AppHostTestFailureException)
        {
            throw;
        }
        catch (Exception failure)
        {
            throw _diagnostics.CaptureFailure(
                "resource.restart",
                resourceName,
                failure);
        }
    }

    private static CancellationTokenSource CreateOperationToken(
        CancellationToken cancellationToken)
    {
        var operation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(OperationTimeout);
        return operation;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Cleanup must attempt every graph-owned stage, retain the primary original failure, and release the sole lease last.")]
    private async Task DisposeCoreAsync()
    {
        Exception? primaryFailure = null;
        using var cleanup = CancellationTokenSource
            .CreateLinkedTokenSource(CancellationToken.None);
        cleanup.CancelAfter(CleanupTimeout);

        // Aspire 13.4.6 host StopAsync cancels its DCP resource watcher before
        // stopping resource processes. Drive snapshot-advertised instance stop
        // commands first so terminal evidence is observable without polling.
        _diagnostics.RecordCleanup("resource-stop", "running");
        try
        {
            CaptureCurrentStates();
            await StopRuntimeResourcesAsync(cleanup.Token);
            _diagnostics.RecordCleanup("resource-stop", "succeeded");
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            _diagnostics.RecordCleanup(
                "resource-stop",
                "failed",
                failure);
        }

        _diagnostics.RecordCleanup(
            "terminal-state",
            "running");
        try
        {
            CaptureCurrentStates();
            var terminalTasks = _diagnostics
                .RuntimeResourceIds()
                .Select(resourceId =>
                    _diagnostics.WaitForTerminalAsync(
                        resourceId,
                        cleanup.Token));
            await Task.WhenAll(terminalTasks);
            _diagnostics.RecordCleanup(
                "terminal-state",
                "succeeded");
        }
        catch (Exception failure)
        {
            primaryFailure ??= failure;
            _diagnostics.RecordCleanup(
                "terminal-state",
                "failed",
                failure);
        }

        _diagnostics.RecordCleanup("application-stop", "running");
        try
        {
            await _application.StopAsync(cleanup.Token);
            CaptureCurrentStates();
            _diagnostics.RecordCleanup(
                "application-stop",
                "succeeded");
        }
        catch (Exception failure)
        {
            primaryFailure ??= failure;
            _diagnostics.RecordCleanup(
                "application-stop",
                "failed",
                failure);
        }

        _diagnostics.RecordCleanup("collectors", "running");
        try
        {
            await _collectorCancellation.CancelAsync();

            Task[] collectorTasks;
            lock (_collectorSync)
            {
                collectorTasks =
                [
                    _notificationCollector,
                    .. _logCollectors.Values,
                ];
            }

            await Task.WhenAll(collectorTasks);
            _diagnostics.RecordCleanup(
                "collectors",
                "succeeded");
        }
        catch (Exception failure)
        {
            primaryFailure ??= failure;
            _diagnostics.RecordCleanup(
                "collectors",
                "failed",
                failure);
        }
        finally
        {
            _collectorCancellation.Dispose();
        }

        _diagnostics.RecordCleanup(
            "application-dispose",
            "running");
        try
        {
            await _application.DisposeAsync();
            _diagnostics.RecordCleanup(
                "application-dispose",
                "succeeded");
        }
        catch (Exception failure)
        {
            primaryFailure ??= failure;
            _diagnostics.RecordCleanup(
                "application-dispose",
                "failed",
                failure);
        }

        if (primaryFailure is not null)
        {
            _diagnostics.RecordCleanup("complete", "failed");
            throw _diagnostics.CaptureFailure(
                "graph.cleanup",
                requestedResource: null,
                primaryFailure);
        }

        _diagnostics.RecordCleanup("complete", "succeeded");
    }

    private async Task StopRuntimeResourcesAsync(
        CancellationToken cancellationToken)
    {
        var stoppableResourceIds = _diagnostics
            .RuntimeResourceIds()
            .Where(resourceId => !ResourceHasTerminalState(resourceId))
            .Where(ResourceAdvertisesStopCommand)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Task.WhenAll(
            stoppableResourceIds.Select(
                resourceId => StopRuntimeResourceAsync(
                    resourceId,
                    cancellationToken)));
    }

    private bool ResourceAdvertisesStopCommand(
        string resourceId)
        => _application.ResourceNotifications.TryGetCurrentState(
                resourceId,
                out var resourceEvent)
            && resourceEvent is not null
            && resourceEvent.Snapshot.Commands.Any(
                command => string.Equals(
                    command.Name,
                    KnownResourceCommands.StopCommand,
                    StringComparison.Ordinal));

    private async Task StopRuntimeResourceAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        _diagnostics.RecordCommand(
            resourceId,
            KnownResourceCommands.StopCommand,
            "requested",
            detail: null);

        ExecuteCommandResult result;
        try
        {
            result = await _application.ResourceCommands
                .ExecuteCommandAsync(
                    resourceId,
                    KnownResourceCommands.StopCommand,
                    cancellationToken);
        }
        catch (Exception failure)
        {
            _diagnostics.RecordCommand(
                resourceId,
                KnownResourceCommands.StopCommand,
                "failed",
                $"{failure.GetType().FullName}: {failure.Message}");
            throw;
        }

#pragma warning disable CS0618 // Required failure evidence from the pinned Aspire 13.4.6 result.
        var errorMessage = result.ErrorMessage;
#pragma warning restore CS0618
        var terminalAfterResult = ResourceHasTerminalState(resourceId);
        _diagnostics.RecordCommand(
            resourceId,
            KnownResourceCommands.StopCommand,
            result.Success
                ? "succeeded"
                : terminalAfterResult
                    ? "superseded-terminal"
                    : "failed",
            $"ErrorMessage='{errorMessage ?? "(null)"}', "
            + $"Message='{result.Message ?? "(null)"}', "
            + $"Canceled={result.Canceled}");

        if (!result.Success && !terminalAfterResult)
        {
            throw new InvalidOperationException(
                $"Stop failed for resource instance '{resourceId}': "
                + $"ErrorMessage='{errorMessage ?? "(null)"}', "
                + $"Message='{result.Message ?? "(null)"}', "
                + $"Canceled={result.Canceled}, "
                + $"CurrentState={DescribeResourceState(resourceId)}.");
        }
    }

    private bool ResourceHasTerminalState(string resourceId)
        => _application.ResourceNotifications.TryGetCurrentState(
                resourceId,
                out var resourceEvent)
            && resourceEvent?.Snapshot.State?.Text is { } state
            && KnownResourceStates.TerminalStates.Contains(
                state,
                StringComparer.Ordinal);

    private string DescribeResourceState(string resourceName)
    {
        if (!_application.ResourceNotifications.TryGetCurrentState(
                resourceName,
                out var resourceEvent)
            || resourceEvent is null)
        {
            return "unavailable";
        }

        var snapshot = resourceEvent.Snapshot;
        return $"state='{snapshot.State?.Text ?? "(null)"}', "
            + $"health='{snapshot.HealthStatus?.ToString() ?? "(null)"}'";
    }

    private void EnsureLogCollector(string resourceName)
    {
        lock (_collectorSync)
        {
            if (_logCollectors.ContainsKey(resourceName))
            {
                return;
            }

            if (_resourceLogger is null)
            {
                var failure = new InvalidOperationException(
                    "Aspire ResourceLoggerService is unavailable.");
                _diagnostics.RecordCollectorFailure(
                    $"logs.{resourceName}",
                    failure);
                _logCollectors.Add(
                    resourceName,
                    Task.FromException(failure));
                return;
            }

            _logCollectors.Add(
                resourceName,
                CollectLogsAsync(
                    resourceName,
                    _collectorCancellation.Token));
        }
    }

    private void CaptureCurrentStates()
    {
        foreach (var resourceName in _resourceNames)
        {
            if (_application.ResourceNotifications.TryGetCurrentState(
                    resourceName,
                    out var resourceEvent)
                && resourceEvent is not null)
            {
                RecordNotification(resourceEvent);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Collector failures are bounded evidence and must also fault cleanup; expected cancellation is handled separately.")]
    private async Task CollectNotificationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var resourceEvent in
                           _application.ResourceNotifications
                               .WatchAsync(cancellationToken))
            {
                RecordNotification(resourceEvent);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Expected graph-owned collector shutdown.
        }
        catch (Exception failure)
        {
            _diagnostics.RecordCollectorFailure(
                "notifications",
                failure);
            _diagnostics.FailTerminalWaiters(failure);
            throw;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Collector failures are bounded evidence and must also fault cleanup; expected cancellation is handled separately.")]
    private async Task CollectLogsAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var batch in _resourceLogger!
                               .WatchAsync(resourceName)
                               .WithCancellation(cancellationToken))
            {
                foreach (var line in batch)
                {
                    _diagnostics.RecordLog(
                        resourceName,
                        line.Content,
                        line.IsErrorMessage);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Expected graph-owned collector shutdown.
        }
        catch (Exception failure)
        {
            _diagnostics.RecordCollectorFailure(
                $"logs.{resourceName}",
                failure);
            throw;
        }
    }

    private void RecordNotification(ResourceEvent resourceEvent)
    {
        var snapshot = resourceEvent.Snapshot;
        var timestamp = snapshot.StopTimeStamp
            ?? snapshot.StartTimeStamp
            ?? snapshot.CreationTimeStamp;

        _diagnostics.RecordNotification(
            resourceEvent.ResourceId,
            string.IsNullOrWhiteSpace(snapshot.ResourceType)
                ? resourceEvent.Resource.GetType().Name
                : snapshot.ResourceType,
            snapshot.State?.Text,
            snapshot.HealthStatus?.ToString(),
            timestamp is null
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(timestamp.Value),
            snapshot.ExitCode,
            snapshot.Urls.Select(url => url.Url));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

    private async Task WaitUntilHealthyCoreAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        await _application.ResourceNotifications
            .WaitForResourceHealthyAsync(
                resourceName,
                WaitBehavior.StopOnResourceUnavailable,
                cancellationToken);
    }
}
