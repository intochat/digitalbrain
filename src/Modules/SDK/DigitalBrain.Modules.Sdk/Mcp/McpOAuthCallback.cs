using DigitalBrain.Abstractions;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpOAuthCallback
{
    private static readonly TimeSpan GrainPollInterval = TimeSpan.FromMilliseconds(50);

    internal static async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        McpOAuthSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        var state = QueryValue(context.AuthorizationUri, "state")
            ?? throw new InvalidOperationException("The OAuth authorization URI contains no state value.");

        McpAuthorizationCodeHub.RegisterSession(state, session);

        var authorization = session.Grains.GetGrain<IMcpAuthorization>(
            NeuronId.For<IMcpAuthorization>(session.Owner, McpAuthorizationNeuron.InstanceName).ToGrainId());

        await authorization.Begin(
            new BeginMcpAuthorization(
                session.CommandId,
                session.ServerKey,
                session.ServerDisplayName,
                context.AuthorizationUri,
                state),
            cancellationToken).ConfigureAwait(true);

        return await AwaitDeliveredCodeAsync(state, session, authorization, cancellationToken).ConfigureAwait(true);
    }

    private static async Task<AuthorizationResult?> AwaitDeliveredCodeAsync(
        string state,
        McpOAuthSession session,
        IMcpAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var hubTask = McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken);
        while (!hubTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Cancellation.ThrowIfCancellationRequested();

            if (await IsDeniedAsync(authorization, session.CommandId, cancellationToken).ConfigureAwait(true))
            {
                throw new OperationCanceledException(
                    "MCP authorization ended without a code; the pending session open was canceled.");
            }

            var taken = await authorization.TakeCompletedCode(state, cancellationToken).ConfigureAwait(true);
            if (taken is not null)
            {
                return ToAuthorizationResult(taken, state);
            }

            await Task.WhenAny(hubTask, Task.Delay(GrainPollInterval, cancellationToken)).ConfigureAwait(true);
        }

        try
        {
            var delivered = await hubTask.ConfigureAwait(true);
            if (delivered is null)
            {
                throw new OperationCanceledException(
                    "MCP authorization ended without a code; the pending session open was canceled.");
            }

            return ToAuthorizationResult(delivered, state);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        if (await IsDeniedAsync(authorization, session.CommandId, cancellationToken).ConfigureAwait(true))
        {
            throw new OperationCanceledException(
                "MCP authorization ended without a code; the pending session open was canceled.");
        }

        var afterHub = await authorization.TakeCompletedCode(state, cancellationToken).ConfigureAwait(true);
        if (afterHub is null)
        {
            throw new OperationCanceledException(
                "MCP authorization ended without a code; the pending session open was canceled.");
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
            var claim = await authorization.Claim(commandId, cancellationToken).ConfigureAwait(true);
            return claim.Kind is McpAuthorizationClaimKind.Denied;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static AuthorizationResult ToAuthorizationResult(
        McpAuthorizationCodeResult delivered,
        string state)
        => new()
        {
            Code = delivered.Code,
            State = state,
            Iss = delivered.Iss,
        };

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
