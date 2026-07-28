using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Mcp;

internal static class McpOAuthOptions
{
    internal static ClientOAuthOptions Create(
        McpServerDefinition server,
        IConfiguration configuration,
        ITokenCache tokenCache)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(tokenCache);

        return new ClientOAuthOptions
        {
            ClientId = Required(configuration, server, "ClientId"),
            ClientSecret = server.RequiresClientSecret
                ? Required(configuration, server, "ClientSecret")
                : Optional(configuration, server, "ClientSecret"),
            RedirectUri = RequiredUri(configuration, server, "RedirectUri"),
            Scopes = server.Scopes,
            TokenCache = tokenCache,
            AuthorizationRedirectDelegate = McpAuthorizationRedirect.Create(configuration),
        };
    }

    private static string? Optional(
        IConfiguration configuration,
        McpServerDefinition server,
        string name)
    {
        var value = configuration[$"{server.ConfigurationRoot}:{name}"];
        return !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static string Required(
        IConfiguration configuration,
        McpServerDefinition server,
        string name)
    {
        var key = $"{server.ConfigurationRoot}:{name}";
        var value = configuration[key];

        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"{server.DisplayName} requires projected configuration '{key}'.");
    }

    private static Uri RequiredUri(
        IConfiguration configuration,
        McpServerDefinition server,
        string name)
    {
        var value = Required(configuration, server, name);

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"{server.ConfigurationRoot}:{name} must be an absolute URI.");
    }
}
