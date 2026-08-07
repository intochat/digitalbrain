using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpAuthorizationCallback
{
    internal static Func<AuthorizationCallbackContext, CancellationToken, Task<AuthorizationResult?>> Create(
        IConfiguration configuration,
        McpAuthorizationAmbientState? ambient = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _ = configuration;

        return (context, cancellationToken) =>
            BrowserSignInCallback.AuthorizeAsync(context, ambient, cancellationToken);
    }
}
