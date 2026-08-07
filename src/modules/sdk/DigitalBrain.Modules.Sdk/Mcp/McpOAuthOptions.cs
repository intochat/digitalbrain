using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpOAuthOptions
{
    internal static ClientOAuthOptions Create(
        McpServerDefinition server,
        IConfiguration configuration,
        ITokenCache tokenCache,
        McpOAuthSession session)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(tokenCache);
        ArgumentNullException.ThrowIfNull(session);

        return new ClientOAuthOptions
        {
            ClientId = Required(configuration, server, "ClientId"),
            ClientSecret = server.RequiresClientSecret
                ? Required(configuration, server, "ClientSecret")
                : Optional(configuration, server, "ClientSecret"),
            RedirectUri = RequiredUri(configuration, server, "RedirectUri"),
            Scopes = server.Scopes,
            TokenCache = tokenCache,
            AuthorizationCallbackHandler = (context, cancellationToken) =>
                McpOAuthCallback.AuthorizeAsync(context, session, cancellationToken),
        };
    }

    private static string? Optional(IConfiguration configuration, McpServerDefinition server, string name)
    {
        var key = $"{server.ConfigurationRoot}:{name}";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        RejectPlaceholder(server, key, value);
        return value;
    }

    private static string Required(IConfiguration configuration, McpServerDefinition server, string name)
    {
        var key = $"{server.ConfigurationRoot}:{name}";
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} requires projected configuration '{key}'.");
        }

        RejectPlaceholder(server, key, value);
        return value;
    }

    private static void RejectPlaceholder(McpServerDefinition server, string key, string value)
    {
        if (string.Equals(value, "local-dev", StringComparison.Ordinal)
            || string.Equals(value, "local-dev-secret", StringComparison.Ordinal)
            || string.Equals(value, "http://localhost/oauth/callback", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} is using a disallowed placeholder for '{key}'. Configure a real application credential.");
        }
    }

    private static Uri RequiredUri(IConfiguration configuration, McpServerDefinition server, string name)
    {
        var value = Required(configuration, server, name);

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"{server.ConfigurationRoot}:{name} must be an absolute URI.");
    }
}
