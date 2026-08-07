using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

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

    internal static void UnregisterAmbient(McpAuthorizationAmbientState ambient)
    {
        ArgumentNullException.ThrowIfNull(ambient);
        AmbientsByCommand.TryRemove(ambient.CommandId.Value, out _);
        foreach (var pair in AmbientsByState.ToArray())
        {
            if (ReferenceEquals(pair.Value, ambient))
            {
                AmbientsByState.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <summary>
    /// Completes the hub waiter for <paramref name="state"/>. A null result is a no-code outcome:
    /// the matching ambient (if any) is aborted so hold-open CreateAsync is abandoned.
    /// Unknown/foreign states complete only the hub — they must not abort a live park for a
    /// different state.
    /// </summary>
    internal static void Complete(string state, McpAuthorizationCodeResult? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var outcome = result is null
            ? CodeHubOutcome.AsNoCode()
            : CodeHubOutcome.AsCompleted(result);

        Completions[state] = outcome;
        if (Waiters.TryGetValue(state, out var waiter))
        {
            waiter.TrySetResult(outcome);
        }

        if (result is null)
        {
            if (AmbientsByState.TryRemove(state, out var ambient))
            {
                AmbientsByCommand.TryRemove(ambient.CommandId.Value, out _);
                ambient.AbortOpen();
            }

            return;
        }

        if (AmbientsByState.TryRemove(state, out var completed))
        {
            AmbientsByCommand.TryRemove(completed.CommandId.Value, out _);
        }
    }

    /// <summary>
    /// Aborts the hold-open ambient for <paramref name="commandId"/> by command identity.
    /// Authoritative for deny/cancel when OAuth state keys diverge from the register key.
    /// </summary>
    internal static void AbortOpen(CommandId commandId)
    {
        if (AmbientsByCommand.TryRemove(commandId.Value, out var ambient))
        {
            UnregisterAmbient(ambient);
            ambient.AbortOpen();
        }
    }

    /// <summary>
    /// Releases every in-process hold-open OAuth attempt (teardown / tests).
    /// </summary>
    internal static void AbortAllOpenSessions()
    {
        foreach (var waiter in Waiters.Values.ToArray())
        {
            waiter.TrySetResult(CodeHubOutcome.AsNoCode());
        }

        Waiters.Clear();

        foreach (var ambient in AmbientsByCommand.Values.ToArray())
        {
            ambient.AbortOpen();
        }

        AmbientsByCommand.Clear();
        AmbientsByState.Clear();
    }

    /// <summary>
    /// Test isolation: drop static waiters/completions/ambients so a parked session cannot leak
    /// across method scopes or fixture lifetime.
    /// </summary>
    internal static void ResetForTests()
    {
        AbortAllOpenSessions();
        Completions.Clear();
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

        await using (cancellationToken.Register(
            static target => ((TaskCompletionSource<CodeHubOutcome>)target!).TrySetCanceled(),
            waiter).ConfigureAwait(false))
        {
            try
            {
                var outcome = await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                Completions.TryRemove(state, out _);
                return outcome.ToResult();
            }
            finally
            {
                Waiters.TryRemove(state, out _);
            }
        }
    }

    private readonly struct CodeHubOutcome
    {
        private CodeHubOutcome(bool withoutCode, McpAuthorizationCodeResult? result)
        {
            WithoutCode = withoutCode;
            Result = result;
        }

        private bool WithoutCode { get; }
        private McpAuthorizationCodeResult? Result { get; }

        internal static CodeHubOutcome AsNoCode() => new(withoutCode: true, result: null);

        internal static CodeHubOutcome AsCompleted(McpAuthorizationCodeResult result)
            => new(withoutCode: false, result: result);

        internal McpAuthorizationCodeResult? ToResult() => WithoutCode ? null : Result;
    }
}
