using System.Collections.Concurrent;

namespace DigitalBrain.Mcp;

internal static class McpAuthorizationCodeHub
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<McpAuthorizationCodeResult?>> Waiters =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, McpAuthorizationCodeResult?> Completions =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, McpAuthorizationAmbientState> Ambients =
        new(StringComparer.Ordinal);
    private static McpAuthorizationAmbientState? _activeAmbient;

    internal static void RegisterAmbient(string state, McpAuthorizationAmbientState ambient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(ambient);
        Ambients[state] = ambient;
        Ambients[ambient.CommandId.ToString()] = ambient;
        Volatile.Write(ref _activeAmbient, ambient);
        // If the edge already completed (race), surface it immediately.
        if (Completions.TryGetValue(state, out var prior))
        {
            ambient.CodeReady.TrySetResult(prior);
        }
    }

    internal static void Complete(string state, McpAuthorizationCodeResult? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        Completions[state] = result;
        if (Waiters.TryGetValue(state, out var waiter))
        {
            waiter.TrySetResult(result);
        }

        var active = Volatile.Read(ref _activeAmbient);
        if (active is not null)
        {
            active.CodeReady.TrySetResult(result);
        }

        foreach (var ambient in Ambients.Values.Distinct())
        {
            ambient.CodeReady.TrySetResult(result);
        }

        Ambients.Clear();
    }

    internal static async Task<McpAuthorizationCodeResult?> AwaitAsync(string state, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        if (Completions.TryRemove(state, out var already))
        {
            return already;
        }

        var waiter = Waiters.GetOrAdd(
            state,
            static _ => new TaskCompletionSource<McpAuthorizationCodeResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously));

        if (Completions.TryRemove(state, out already))
        {
            waiter.TrySetResult(already);
            Waiters.TryRemove(state, out _);
            return already;
        }

        await using var registration = cancellationToken.Register(
            static target => ((TaskCompletionSource<McpAuthorizationCodeResult?>)target!).TrySetCanceled(),
            waiter);
        try
        {
            var result = await waiter.Task.WaitAsync(cancellationToken);
            Completions.TryRemove(state, out _);
            return result;
        }
        finally
        {
            Waiters.TryRemove(state, out _);
        }
    }
}
