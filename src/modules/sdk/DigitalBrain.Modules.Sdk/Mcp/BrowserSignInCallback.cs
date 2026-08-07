using DigitalBrain.Abstractions;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Modules.Sdk.Mcp;

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
            await ambient.BeginCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await AwaitDeliveredCodeAsync(state, ambient, cancellationToken).ConfigureAwait(false);
        }

        return ToAuthorizationResult(
            await McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken).ConfigureAwait(false),
            state);
    }

    private static async Task<AuthorizationResult?> AwaitDeliveredCodeAsync(
        string state,
        McpAuthorizationAmbientState ambient,
        CancellationToken cancellationToken)
    {
        var authorization = ambient.Grains.GetGrain<IMcpAuthorization>(
            NeuronId.For<IMcpAuthorization>(ambient.Owner, McpAuthorizationNeuron.InstanceName).ToGrainId());

        // Hub + terminal + durable claim/code. Any no-code outcome must throw OCE so CreateAsync
        // does not park on session Completion after a null AuthorizationResult.
        var hubTask = McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken);
        var terminalTask = ambient.Terminal.Task;
        while (!hubTask.IsCompleted && !terminalTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsDeniedAsync(authorization, ambient.CommandId, cancellationToken).ConfigureAwait(false))
            {
                return FailNoCode(ambient);
            }

            var taken = await authorization.TakeCompletedCode(state, cancellationToken).ConfigureAwait(false);
            if (taken is not null)
            {
                return ToAuthorizationResult(taken, state);
            }

            await Task.WhenAny(hubTask, terminalTask, Task.Delay(GrainPollInterval, cancellationToken)).ConfigureAwait(false);
        }

        if (terminalTask.IsCompleted || await IsDeniedAsync(authorization, ambient.CommandId, cancellationToken).ConfigureAwait(false))
        {
            return FailNoCode(ambient);
        }

        try
        {
            var hubDelivered = await hubTask.ConfigureAwait(false);
            if (hubDelivered is null)
            {
                return FailNoCode(ambient);
            }

            return ToAuthorizationResult(hubDelivered, state);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Hub waiter canceled without the outer token — fall through to grain.
        }

        if (await IsDeniedAsync(authorization, ambient.CommandId, cancellationToken).ConfigureAwait(false))
        {
            return FailNoCode(ambient);
        }

        var afterHub = await authorization.TakeCompletedCode(state, cancellationToken).ConfigureAwait(false);
        if (afterHub is null)
        {
            return FailNoCode(ambient);
        }

        return ToAuthorizationResult(afterHub, state);
    }

    private static async Task<bool> IsDeniedAsync(
        IMcpAuthorization authorization,
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        try
        {
            var claim = await authorization.Claim(commandId, cancellationToken).ConfigureAwait(false);
            return claim.Kind is McpAuthorizationClaimKind.Denied;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static AuthorizationResult? FailNoCode(McpAuthorizationAmbientState ambient)
    {
        ambient.AbortOpen();
        // Returning null makes MCP SDK 2.0 CreateAsync await session Completion after the OAuth
        // failure, which can park forever. OperationCanceledException is excluded from that path.
        throw new OperationCanceledException(
            "MCP authorization ended without a code; the pending session open was canceled.");
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
