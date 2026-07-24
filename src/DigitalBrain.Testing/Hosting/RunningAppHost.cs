using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

public sealed class RunningAppHost : IAsyncDisposable
{
    private const string RestartCommand = "resource-restart";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(5);
    private readonly DistributedApplication _application;
    private readonly AppHostExclusiveLease _lease;
    private readonly Action<RunningAppHost> _release;
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
        var resourceNames = model.Resources
            .Select(resource => resource.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        _resourceNames = resourceNames.ToHashSet(StringComparer.Ordinal);
        _resourceNamesDisplay = string.Join(", ", resourceNames);
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
                throw new InvalidOperationException(
                    $"Resource '{name}' does not exist. Known resources: {_resourceNamesDisplay}.");
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

    internal HttpClient CreateHttpClient(
        string resourceName,
        string? endpointName)
    {
        ThrowIfDisposed();
        return _application.CreateHttpClient(resourceName, endpointName);
    }

    internal async Task WaitUntilHealthyAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operation = CreateOperationToken(cancellationToken);
        await WaitUntilHealthyCoreAsync(resourceName, operation.Token);
    }

    internal async Task RestartAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operation = CreateOperationToken(cancellationToken);
        var result = await _application.ResourceCommands.ExecuteCommandAsync(
            resourceName,
            RestartCommand,
            operation.Token);

        if (!result.Success)
        {
#pragma warning disable CS0618 // Required failure evidence from the pinned Aspire 13.4.6 result.
            var errorMessage = result.ErrorMessage;
#pragma warning restore CS0618
            throw new InvalidOperationException(
                $"Restart failed for resource '{resourceName}': "
                + $"ErrorMessage='{errorMessage ?? "(null)"}', "
                + $"Message='{result.Message ?? "(null)"}', "
                + $"Canceled={result.Canceled}, "
                + $"CurrentState={DescribeResourceState(resourceName)}.");
        }

        await WaitUntilHealthyCoreAsync(resourceName, operation.Token);
    }

    private static CancellationTokenSource CreateOperationToken(
        CancellationToken cancellationToken)
    {
        var operation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(OperationTimeout);
        return operation;
    }

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

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

    private async Task WaitUntilHealthyCoreAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        await _application.ResourceNotifications.WaitForResourceHealthyAsync(
            resourceName,
            WaitBehavior.StopOnResourceUnavailable,
            cancellationToken);
    }
}
