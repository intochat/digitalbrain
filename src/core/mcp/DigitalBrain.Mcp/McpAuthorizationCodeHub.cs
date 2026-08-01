using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Mcp;

internal static class McpAuthorizationCodeHub
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<CodeHubOutcome>> Waiters =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CodeHubOutcome> Completions =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, McpAuthorizationAmbientState> AmbientsByState =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Guid, McpAuthorizationAmbientState> AmbientsByCommand = new();

    internal static void RegisterAmbient(string state, McpAuthorizationAmbientState ambient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(ambient);
        AmbientsByState[state] = ambient;
        AmbientsByCommand[ambient.CommandId.Value] = ambient;
    }

    internal static void Complete(string state, McpAuthorizationCodeResult? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var outcome = result is null
            ? CodeHubOutcome.AsDenied()
            : CodeHubOutcome.AsCompleted(result);

        Completions[state] = outcome;
        if (Waiters.TryGetValue(state, out var waiter))
        {
            waiter.TrySetResult(outcome);
        }

        if (result is null)
        {
            // Deny must release every hold-open ambient in this process. State-key mismatch between
            // the authorize URL and callback must not leave Task.Run parked.
            foreach (var open in AmbientsByCommand.Values.ToArray())
            {
                open.SignalDenied();
            }

            AmbientsByCommand.Clear();
            AmbientsByState.Clear();
            return;
        }

        if (AmbientsByState.TryRemove(state, out var ambient))
        {
            AmbientsByCommand.TryRemove(ambient.CommandId.Value, out _);
        }
    }

    internal static void SignalDenied(CommandId commandId)
    {
        if (AmbientsByCommand.TryRemove(commandId.Value, out var ambient))
        {
            ambient.SignalDenied();
        }
    }

    internal static async Task<McpAuthorizationCodeResult?> AwaitAsync(string state, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        if (Completions.TryRemove(state, out var already))
        {
            return already.ToResult();
        }

        var waiter = Waiters.GetOrAdd(
            state,
            static _ => new TaskCompletionSource<CodeHubOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously));

        if (Completions.TryRemove(state, out already))
        {
            waiter.TrySetResult(already);
            Waiters.TryRemove(state, out _);
            return already.ToResult();
        }

        await using var registration = cancellationToken.Register(
            static target => ((TaskCompletionSource<CodeHubOutcome>)target!).TrySetCanceled(),
            waiter);
        try
        {
            var outcome = await waiter.Task.WaitAsync(cancellationToken);
            Completions.TryRemove(state, out _);
            return outcome.ToResult();
        }
        finally
        {
            Waiters.TryRemove(state, out _);
        }
    }

    private readonly struct CodeHubOutcome
    {
        private CodeHubOutcome(bool denied, McpAuthorizationCodeResult? result)
        {
            Denied = denied;
            Result = result;
        }

        private bool Denied { get; }
        private McpAuthorizationCodeResult? Result { get; }

        internal static CodeHubOutcome AsDenied() => new(denied: true, result: null);

        internal static CodeHubOutcome AsCompleted(McpAuthorizationCodeResult result)
            => new(denied: false, result: result);

        internal McpAuthorizationCodeResult? ToResult() => Denied ? null : Result;
    }
}
