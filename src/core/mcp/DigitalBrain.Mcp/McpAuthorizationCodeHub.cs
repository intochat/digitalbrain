using System.Collections.Concurrent;

namespace DigitalBrain.Mcp;

internal static class McpAuthorizationCodeHub
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<McpAuthorizationCodeResult?>> Waiters =
        new(StringComparer.Ordinal);

    internal static TaskCompletionSource<McpAuthorizationCodeResult?> WaiterFor(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return Waiters.GetOrAdd(
            state,
            static _ => new TaskCompletionSource<McpAuthorizationCodeResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously));
    }

    internal static void Complete(string state, McpAuthorizationCodeResult? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        WaiterFor(state).TrySetResult(result);
        Waiters.TryRemove(state, out _);
    }

    internal static async Task<McpAuthorizationCodeResult?> AwaitAsync(string state, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        var waiter = WaiterFor(state);
        await using var registration = cancellationToken.Register(
            static target => ((TaskCompletionSource<McpAuthorizationCodeResult?>)target!).TrySetCanceled(),
            waiter);
        return await waiter.Task.WaitAsync(cancellationToken);
    }
}
