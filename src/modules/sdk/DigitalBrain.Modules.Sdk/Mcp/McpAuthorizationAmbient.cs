using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpAuthorizationAmbient
{
    private static readonly AsyncLocal<McpAuthorizationAmbientState?> Current = new();

    internal static McpAuthorizationAmbientState? State => Current.Value;

    internal static IDisposable Enter(McpAuthorizationAmbientState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = Current.Value;
        Current.Value = state;
        return new Restorer(previous);
    }

    private sealed class Restorer(McpAuthorizationAmbientState? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
internal sealed class McpAuthorizationAmbientState
{
    private readonly CancellationTokenSource _openLifetime = new();

    internal McpAuthorizationAmbientState(
        CommandId commandId,
        string serverKey,
        string serverDisplayName,
        OwnerId owner,
        IGrainFactory grains)
    {
        CommandId = commandId;
        ServerKey = serverKey;
        ServerDisplayName = serverDisplayName;
        Owner = owner;
        Grains = grains;
        SignInReady = new TaskCompletionSource<McpAuthorizationSignIn>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        BeginCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal CommandId CommandId { get; }
    internal string ServerKey { get; }
    internal string ServerDisplayName { get; }
    internal OwnerId Owner { get; }
    internal IGrainFactory Grains { get; }
    internal TaskCompletionSource<McpAuthorizationSignIn> SignInReady { get; }
    internal TaskCompletionSource BeginCompleted { get; }
    internal TaskCompletionSource Terminal { get; }
    internal CancellationToken OpenCancellation => _openLifetime.Token;

    internal void AbortOpen()
    {
        Terminal.TrySetResult();
        BeginCompleted.TrySetResult();
        CancelOpen();
    }

    internal void CancelOpen()
    {
        try
        {
            _openLifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

internal sealed record McpAuthorizationSignIn(Uri SignInUrl, string State);
