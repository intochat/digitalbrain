using DigitalBrain.Abstractions;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpOAuthCallback
{
    private static readonly TimeSpan GrainPollInterval = TimeSpan.FromMilliseconds(50);

    // The authorization rail is the sole state mint (PKCE). When the MCP client library
    // still invokes this handler, we never mint a second state from AuthorizationUri —
    // we recover the rail's pending transaction for the command and await that state.
    // Codes never cross ClientEntryPoint — hub (in-process) or host-only codes grain.
    internal static async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        McpOAuthSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        var grainId = NeuronId.For<IMcpAuthorization>(session.Owner, McpAuthorizationNeuron.InstanceName).ToGrainId();
        var authorization = session.Grains.GetGrain<IMcpAuthorization>(grainId);
        var codes = session.Grains.GetGrain<IMcpAuthorizationCodes>(grainId);

        // Recover the single rail-minted transaction for this command (no second Begin).
        if (session.Actor is not { } actor)
        {
            throw new InvalidOperationException(
                $"{session.ServerDisplayName} authorization session has no actor for command '{session.CommandId}'.");
        }

        McpAuthorizationClaim claim;
        try
        {
            claim = await authorization.Claim(session.CommandId, actor, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (NeuronAuthorizationException)
        {
            throw new InvalidOperationException(
                $"{session.ServerDisplayName} has no rail-minted authorization for command '{session.CommandId}'. "
                + "Complete McpAuthorizationRail.EnsureAuthorizedAsync first — the library path does not mint state.");
        }

        if (claim.Kind is McpAuthorizationClaimKind.Denied)
        {
            throw new OperationCanceledException(
                "MCP authorization ended without a code; the pending session open was canceled.");
        }

        if (claim.Kind is McpAuthorizationClaimKind.Required && claim.Required is { } required)
        {
            McpAuthorizationCodeHub.RegisterSession(required.State, session);
            return await AwaitDeliveredCodeAsync(
                required.State, session, actor, authorization, codes, cancellationToken)
                .ConfigureAwait(true);
        }

        if (claim.Kind is McpAuthorizationClaimKind.Completed)
        {
            // Code already delivered; recover state via idempotent Begin return shape.
            var recovered = await authorization.Begin(
                new BeginMcpAuthorization(
                    session.CommandId,
                    session.ServerKey,
                    session.ServerDisplayName,
                    new Uri("https://auth.digitalbrain.local/oauth/completed"),
                    "unused-when-command-exists",
                    actor),
                cancellationToken).ConfigureAwait(true);
            var taken = await codes.TakeCompletedCode(recovered.State, cancellationToken).ConfigureAwait(true);
            if (taken is null)
            {
                throw new OperationCanceledException(
                    "MCP authorization ended without a code; the pending session open was canceled.");
            }

            return ToAuthorizationResult(taken, recovered.State);
        }

        throw new InvalidOperationException(
            $"{session.ServerDisplayName} authorization claim kind '{claim.Kind}' is unsupported in the library callback.");
    }

    private static async Task<AuthorizationResult?> AwaitDeliveredCodeAsync(
        string state,
        McpOAuthSession session,
        ActorContext actor,
        IMcpAuthorization authorization,
        IMcpAuthorizationCodes codes,
        CancellationToken cancellationToken)
    {
        var hubTask = McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken);
        while (!hubTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Cancellation.ThrowIfCancellationRequested();

            if (await IsDeniedAsync(authorization, session.CommandId, actor, cancellationToken)
                .ConfigureAwait(true))
            {
                throw new OperationCanceledException(
                    "MCP authorization ended without a code; the pending session open was canceled.");
            }

            var taken = await codes.TakeCompletedCode(state, cancellationToken).ConfigureAwait(true);
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

        if (await IsDeniedAsync(authorization, session.CommandId, actor, cancellationToken)
            .ConfigureAwait(true))
        {
            throw new OperationCanceledException(
                "MCP authorization ended without a code; the pending session open was canceled.");
        }

        var afterHub = await codes.TakeCompletedCode(state, cancellationToken).ConfigureAwait(true);
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
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        try
        {
            var claim = await authorization.Claim(commandId, actor, cancellationToken).ConfigureAwait(true);
            return claim.Kind is McpAuthorizationClaimKind.Denied;
        }
        catch (NeuronAuthorizationException)
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
}
