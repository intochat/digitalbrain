using DigitalBrain.Abstractions;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Mcp;

internal static class BrowserSignInCallback
{
    private static readonly TimeSpan GrainPollInterval = TimeSpan.FromMilliseconds(50);

    internal static Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
        => AuthorizeAsync(context, ambient: null, cancellationToken);

    internal static async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        McpAuthorizationAmbientState? ambient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var state = QueryValue(context.AuthorizationUri, "state")
            ?? throw new InvalidOperationException("The OAuth authorization URI contains no state value.");

        if (ambient is not null)
        {
            // Surface the real provider authorize URL, wait for Begin so DeliverCallback has a
            // pending record, then await the app-callback-delivered code (never robot-GET).
            McpAuthorizationCodeHub.RegisterAmbient(state, ambient);
            ambient.SignInReady.TrySetResult(new McpAuthorizationSignIn(context.AuthorizationUri, state));
            await ambient.BeginCompleted.Task.WaitAsync(cancellationToken);
            return await AwaitDeliveredCodeAsync(state, ambient, cancellationToken);
        }

        return ToAuthorizationResult(
            await McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken),
            state);
    }

    private static async Task<AuthorizationResult?> AwaitDeliveredCodeAsync(
        string state,
        McpAuthorizationAmbientState ambient,
        CancellationToken cancellationToken)
    {
        var authorization = ambient.Grains.GetGrain<IMcpAuthorization>(
            NeuronId.For<IMcpAuthorization>(ambient.Owner, McpAuthorizationNeuron.InstanceName).ToGrainId());

        // Prefer the in-process hub; fall back to durable TakeCompletedCode for activation restarts
        // and any race where the hub signal is missed.
        var hubTask = McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken);
        while (!hubTask.IsCompleted)
        {
            var taken = await authorization.TakeCompletedCode(state, cancellationToken);
            if (taken is not null)
            {
                return ToAuthorizationResult(taken, state);
            }

            await Task.WhenAny(hubTask, Task.Delay(GrainPollInterval, cancellationToken));
        }

        try
        {
            var hubDelivered = await hubTask;
            if (hubDelivered is not null)
            {
                return ToAuthorizationResult(hubDelivered, state);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Hub waiter canceled without the outer token — fall through to grain.
        }

        return ToAuthorizationResult(
            await authorization.TakeCompletedCode(state, cancellationToken),
            state);
    }

    private static AuthorizationResult? ToAuthorizationResult(
        McpAuthorizationCodeResult? delivered,
        string state)
    {
        if (delivered is null)
        {
            return null;
        }

        return new AuthorizationResult
        {
            Code = delivered.Code,
            State = state,
            Iss = delivered.Iss,
        };
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (string.Equals(Uri.UnescapeDataString(pair[0]), name, StringComparison.Ordinal))
            {
                return pair.Length == 2
                    ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                    : string.Empty;
            }
        }

        return null;
    }
}
