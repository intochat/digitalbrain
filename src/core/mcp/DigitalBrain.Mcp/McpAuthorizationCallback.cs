using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Mcp;

internal static class McpAuthorizationCallback
{
    internal static Func<AuthorizationCallbackContext, CancellationToken, Task<AuthorizationResult?>> Create(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return string.Equals(
            configuration[McpRuntimeHosting.AuthorizationModeKey],
            McpRuntimeHosting.LocalLoopbackDevelopmentMode,
            StringComparison.Ordinal)
            ? LocalLoopbackMcpAuthorizationCallback.AuthorizeAsync
            : RejectAsync;
    }

    private static Task<AuthorizationResult?> RejectAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<AuthorizationResult?>(new InvalidOperationException(
            "Interactive MCP authorization is disabled. "
            + "Use a durable pre-authorized token or explicitly enable the local loopback development adapter."));
    }
}
