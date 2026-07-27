using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Integrations.Mcp;

internal static class McpAuthorizationRedirect
{
    internal static AuthorizationRedirectDelegate Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return string.Equals(
            configuration[McpRuntimeHosting.AuthorizationModeKey],
            McpRuntimeHosting.LocalLoopbackDevelopmentMode,
            StringComparison.Ordinal)
            ? LocalLoopbackMcpAuthorizationRedirect.AuthorizeAsync
            : RejectAsync;
    }

    private static Task<string?> RejectAsync(
        Uri authorizationUri,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        ArgumentNullException.ThrowIfNull(redirectUri);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<string?>(new InvalidOperationException(
            "Interactive MCP authorization is disabled. "
            + "Use a durable pre-authorized token or explicitly enable the local loopback development adapter."));
    }
}
