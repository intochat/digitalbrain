using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpAuthorizationCodeHub
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<CodeHubOutcome>> Waiters =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CodeHubOutcome> Completions =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, McpOAuthSession> SessionsByState =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Guid, McpOAuthSession> SessionsByCommand = new();

    internal static void RegisterSession(string state, McpOAuthSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(session);
        SessionsByState[state] = session;
        SessionsByCommand[session.CommandId.Value] = session;
    }

    internal static void UnregisterSession(McpOAuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        SessionsByCommand.TryRemove(session.CommandId.Value, out _);
        foreach (var pair in SessionsByState.ToArray())
        {
            if (ReferenceEquals(pair.Value, session))
            {
                SessionsByState.TryRemove(pair.Key, out _);
            }
        }
    }

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
            if (SessionsByState.TryRemove(state, out var session))
            {
                SessionsByCommand.TryRemove(session.CommandId.Value, out _);
                session.Cancel();
            }

            return;
        }

        if (SessionsByState.TryRemove(state, out var completed))
        {
            SessionsByCommand.TryRemove(completed.CommandId.Value, out _);
        }
    }

    internal static void AbortOpen(CommandId commandId)
    {
        if (SessionsByCommand.TryRemove(commandId.Value, out var session))
        {
            UnregisterSession(session);
            session.Cancel();
        }
    }

    internal static void AbortAllOpenSessions()
    {
        foreach (var waiter in Waiters.Values.ToArray())
        {
            waiter.TrySetResult(CodeHubOutcome.AsNoCode());
        }

        Waiters.Clear();

        foreach (var session in SessionsByCommand.Values.ToArray())
        {
            session.Cancel();
        }

        SessionsByCommand.Clear();
        SessionsByState.Clear();
    }

    internal static void ResetForTests()
    {
        AbortAllOpenSessions();
        Completions.Clear();
    }

    // Characterization seam: Completions has no expiry/eviction (P0-1).
    internal static int CompletionsCountForTests => Completions.Count;

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
