using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

public sealed class HostedScenario : IAsyncDisposable
{
    private readonly DistributedApplication _application;
    private readonly IAsyncDisposable _exclusiveLease;
    private readonly TimeSpan _startupTimeout;
    private readonly IReadOnlyList<string> _trackedProcessNames;
    private readonly HashSet<int> _baselineProcessIds;
    private int _disposed;

    internal HostedScenario(
        DistributedApplication application,
        IAsyncDisposable exclusiveLease,
        TimeSpan startupTimeout,
        IReadOnlyList<string> trackedProcessNames,
        HashSet<int> baselineProcessIds)
    {
        _application = application;
        _exclusiveLease = exclusiveLease;
        _startupTimeout = startupTimeout;
        _trackedProcessNames = trackedProcessNames;
        _baselineProcessIds = baselineProcessIds;
    }

    public DistributedApplication Application
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _application;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Readiness diagnostics must attach resource/endpoint identity to whatever Aspire threw.")]
    public HttpClient CreateHttpClient(string resourceName, string? endpointName = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        string? endpointDisplay = null;
        try
        {
            var endpoint = _application.GetEndpoint(resourceName, endpointName);
            endpointDisplay = endpoint.ToString();
            return _application.CreateHttpClient(resourceName, endpointName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                FormatReadiness(
                    "CreateHttpClient failed",
                    resourceName,
                    endpointName,
                    endpointDisplay,
                    resourceState: TryDescribeResourceState(resourceName)),
                ex);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Readiness diagnostics must attach resource/endpoint identity to whatever Aspire threw.")]
    public async Task WaitHealthyAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        try
        {
            await _application.ResourceNotifications
                .WaitForResourceHealthyAsync(resourceName, cancellationToken)
                .WaitAsync(_startupTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new TimeoutException(
                FormatReadiness(
                    $"Resource did not become healthy within {_startupTimeout}",
                    resourceName,
                    endpointName: null,
                    endpointDisplay: TryDescribeEndpoint(resourceName),
                    resourceState: TryDescribeResourceState(resourceName),
                    resourceLogs: await TryCaptureResourceLogsAsync(resourceName, cancellationToken)),
                ex);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "HTTP readiness polls until the deadline; transport and client-timeout failures are retained as lastFailure.")]
    public async Task WaitHttpReadyAsync(
        string resourceName,
        string path = "/health",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await WaitHealthyAsync(resourceName, cancellationToken);

        var endpoint = TryDescribeEndpoint(resourceName);
        var deadline = DateTimeOffset.UtcNow + _startupTimeout;
        Exception? lastFailure = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = CreateHttpClient(resourceName);
                client.Timeout = TimeSpan.FromSeconds(5);
                using var response = await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastFailure = new InvalidOperationException(
                    $"HTTP {(int)response.StatusCode} from {resourceName}{path} at {endpoint}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException(
            FormatReadiness(
                $"HTTP path '{path}' did not become ready within {_startupTimeout}",
                resourceName,
                endpointName: null,
                endpointDisplay: endpoint,
                resourceState: TryDescribeResourceState(resourceName)),
            lastFailure);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Readiness diagnostics must attach resource identity to whatever Aspire threw.")]
    public async Task RestartResourceAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        try
        {
            await _application.ResourceCommands.ExecuteCommandAsync(
                resourceName,
                "resource-restart",
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                FormatReadiness(
                    "resource-restart failed",
                    resourceName,
                    endpointName: null,
                    endpointDisplay: null,
                    resourceState: TryDescribeResourceState(resourceName)),
                ex);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Dispose must always release the exclusive L2 lease and still surface app dispose failures plus process leaks.")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Exception? disposeError = null;
        try
        {
            await _application.DisposeAsync();
        }
        catch (Exception ex)
        {
            disposeError = ex;
        }

        try
        {
            await _exclusiveLease.DisposeAsync();
        }
        catch (Exception ex)
        {
            disposeError = disposeError is null ? ex : new AggregateException(disposeError, ex);
        }

        var remaining = HostedApplication.SnapshotProcessIds(_trackedProcessNames);
        remaining.ExceptWith(_baselineProcessIds);
        if (remaining.Count > 0)
        {
            var orphans = string.Join(
                ", ",
                remaining.OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture)));
            var leak = new InvalidOperationException(
                $"HostedScenario disposed with orphan process id(s) still running for tracked names [{string.Join(", ", _trackedProcessNames)}]: {orphans}.");
            if (disposeError is not null)
            {
                throw new AggregateException(disposeError, leak);
            }

            throw leak;
        }

        if (disposeError is not null)
        {
            throw disposeError;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Endpoint resolution is best-effort diagnostics when health wait already failed.")]
    private string TryDescribeEndpoint(string resourceName)
    {
        try
        {
            return _application.GetEndpoint(resourceName).ToString();
        }
        catch (Exception endpointError)
        {
            return $"unavailable ({endpointError.GetType().Name}: {endpointError.Message})";
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Resource state is best-effort diagnostics attached to readiness failures.")]
    private string TryDescribeResourceState(string resourceName)
    {
        try
        {
            if (!_application.ResourceNotifications.TryGetCurrentState(resourceName, out var resourceEvent)
                || resourceEvent is null)
            {
                return "state unavailable";
            }

            var snapshot = resourceEvent.Snapshot;
            var text = new StringBuilder(capacity: 128);
            text.Append("state=").Append(snapshot.State?.Text ?? "(null)");
            if (!string.IsNullOrWhiteSpace(snapshot.State?.Style))
            {
                text.Append(" style=").Append(snapshot.State.Style);
            }

            if (snapshot.ExitCode is not null)
            {
                text.Append(" exitCode=").Append(snapshot.ExitCode.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.HealthStatus?.ToString()))
            {
                text.Append(" health=").Append(snapshot.HealthStatus);
            }

            return text.ToString();
        }
        catch (Exception stateError)
        {
            return $"state error ({stateError.GetType().Name}: {stateError.Message})";
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Log capture is best-effort diagnostics attached to readiness failures.")]
    private async Task<string> TryCaptureResourceLogsAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        try
        {
            var logService = _application.Services.GetService<ResourceLoggerService>();
            if (logService is null)
            {
                return "logs unavailable (no ResourceLoggerService)";
            }

            var lines = new List<string>(capacity: 64);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            await foreach (var batch in logService.WatchAsync(resourceName).WithCancellation(timeout.Token))
            {
                foreach (var line in batch)
                {
                    var prefix = line.IsErrorMessage ? "ERR " : "OUT ";
                    lines.Add(prefix + line.Content);
                    if (lines.Count >= 80)
                    {
                        break;
                    }
                }

                if (lines.Count >= 80)
                {
                    break;
                }

                // Backlog often arrives in the first batches; stop once we have content and the watch idles.
                if (lines.Count > 0)
                {
                    break;
                }
            }

            if (lines.Count == 0)
            {
                return "logs empty";
            }

            // Keep the tail — crash reasons are usually last.
            var start = Math.Max(0, lines.Count - 40);
            return string.Join(" | ", lines.Skip(start));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "logs timed out";
        }
        catch (Exception logError)
        {
            return $"logs error ({logError.GetType().Name}: {logError.Message})";
        }
    }

    private static string FormatReadiness(
        string action,
        string resourceName,
        string? endpointName,
        string? endpointDisplay,
        string? resourceState = null,
        string? resourceLogs = null)
    {
        var text = new StringBuilder(action, capacity: 256);
        text.Append(" for resource '").Append(resourceName).Append('\'');
        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            text.Append(" endpoint '").Append(endpointName).Append('\'');
        }

        if (!string.IsNullOrWhiteSpace(endpointDisplay))
        {
            text.Append(" at ").Append(endpointDisplay);
        }

        if (!string.IsNullOrWhiteSpace(resourceState))
        {
            text.Append(" (").Append(resourceState).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(resourceLogs))
        {
            text.Append(" logs: ").Append(resourceLogs);
        }

        text.Append('.');
        return text.ToString();
    }
}
