using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Mcp;

internal static class McpAuthorizationCallback
{
    internal static Func<AuthorizationCallbackContext, CancellationToken, Task<AuthorizationResult?>> Create(
        IConfiguration configuration,
        McpAuthorizationAmbientState? ambient = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var mode = configuration[McpRuntimeHosting.AuthorizationModeKey];
        if (string.Equals(mode, McpRuntimeHosting.LocalLoopbackDevelopmentMode, StringComparison.Ordinal))
        {
            return LocalLoopbackMcpAuthorizationCallback.AuthorizeAsync;
        }

        if (string.Equals(mode, McpRuntimeHosting.EdgeMode, StringComparison.Ordinal))
        {
            // Capture ambient at OpenAsync time — AsyncLocal does not survive Orleans/HTTP continuations.
            return (context, cancellationToken) =>
                EdgeMcpAuthorizationCallback.AuthorizeAsync(context, ambient, cancellationToken);
        }

        return RejectAsync;
    }

    private static Task<AuthorizationResult?> RejectAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<AuthorizationResult?>(new InvalidOperationException(
            "Interactive MCP authorization is disabled. "
            + "Use a durable pre-authorized token, enable Edge authorization, "
            + "or explicitly enable the local loopback development adapter."));
    }
}
