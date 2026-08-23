using System.Collections.Concurrent;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Testing.E2E;

// Tails the AppHost's own log stream for a fixed set of resources so a health-wait timeout
// can report what the resource was actually printing, not just its last known state.
internal sealed class ResourceLogCollector : IAsyncDisposable
{
    private const int RingBufferCapacity = 500;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _linesByResource = new(StringComparer.Ordinal);
    private readonly Task[] _watchers;

    internal ResourceLogCollector(ResourceLoggerService loggerService, IEnumerable<string> resourceNames)
    {
        ArgumentNullException.ThrowIfNull(loggerService);
        ArgumentNullException.ThrowIfNull(resourceNames);

        _watchers = resourceNames
            .Select(resourceName => WatchResourceAsync(loggerService, resourceName, _cancellation.Token))
            .ToArray();
    }

    internal IReadOnlyList<string> LastLines(string resourceName, int count)
        => _linesByResource.TryGetValue(resourceName, out var buffer)
            ? buffer.ToArray().TakeLast(count).ToArray()
            : [];

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(_watchers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
    }

    private async Task WatchResourceAsync(
        ResourceLoggerService loggerService,
        string resourceName,
        CancellationToken cancellationToken)
    {
        var buffer = _linesByResource.GetOrAdd(resourceName, static _ => new ConcurrentQueue<string>());

        try
        {
            await foreach (var batch in loggerService.WatchAsync(resourceName).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                foreach (var line in batch)
                {
                    buffer.Enqueue(line.Content);
                    while (buffer.Count > RingBufferCapacity && buffer.TryDequeue(out _))
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
