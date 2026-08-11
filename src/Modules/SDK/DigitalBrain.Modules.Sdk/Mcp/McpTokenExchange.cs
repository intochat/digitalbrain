using System.Net.Http.Headers;
using System.Text.Json;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpTokenExchange
{
    internal static async Task<TokenContainer> ExchangeAuthorizationCodeAsync(
        McpServerDefinition server,
        IConfiguration configuration,
        IHttpClientFactory httpClients,
        string code,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(httpClients);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        cancellationToken.ThrowIfCancellationRequested();

        var clientId = Required(configuration, server, "ClientId");
        var redirectUri = Required(configuration, server, "RedirectUri");
        var clientSecret = server.RequiresClientSecret
            ? Required(configuration, server, "ClientSecret")
            : configuration[$"{server.ConfigurationRoot}:ClientSecret"];
        var tokenEndpoint = ResolveTokenEndpoint(configuration, server);

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            form["client_secret"] = clientSecret;
        }

        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var http = httpClients.CreateClient(McpClientSessions.HttpClientName);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} token exchange failed ({(int)response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access)
            ? access.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} token exchange returned no access_token.");
        }

        int? expiresIn = root.TryGetProperty("expires_in", out var lifetime) && lifetime.TryGetInt32(out var seconds)
            ? seconds
            : null;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString()
            : null;
        var tokenType = root.TryGetProperty("token_type", out var type)
            ? type.GetString()
            : "Bearer";

        return new TokenContainer
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = tokenType ?? "Bearer",
            ExpiresIn = expiresIn,
            ObtainedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Uri ResolveTokenEndpoint(IConfiguration configuration, McpServerDefinition server)
    {
        var configured = configuration[$"{server.ConfigurationRoot}:TokenEndpoint"];
        if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        if (string.Equals(server.Key, "salesforce", StringComparison.OrdinalIgnoreCase)
            || server.Key.Contains("salesforce", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("https://login.salesforce.com/services/oauth2/token");
        }

        if (McpAuthorizationRail.IsGoogleGmailServer(server.Key))
        {
            return new Uri("https://oauth2.googleapis.com/token");
        }

        throw new InvalidOperationException(
            $"{server.DisplayName} requires '{server.ConfigurationRoot}:TokenEndpoint' for authorization-code exchange.");
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

        return value;
    }
}
