using System.Diagnostics;
using System.Net;
using System.Text;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Integrations.Mcp;

internal sealed class McpServerDefinition
{
    internal McpServerDefinition(
        string key,
        string displayName,
        Uri endpoint,
        string configurationRoot,
        IReadOnlyList<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRoot);
        ArgumentNullException.ThrowIfNull(scopes);

        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("An MCP server endpoint must be an absolute HTTPS URI.", nameof(endpoint));
        }

        if (scopes.Count == 0 || scopes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("An MCP server must declare its non-empty OAuth scopes.", nameof(scopes));
        }

        Key = key;
        DisplayName = displayName;
        Endpoint = endpoint;
        ConfigurationRoot = configurationRoot;
        Scopes = scopes.ToArray();
    }

    internal string Key { get; }

    internal string DisplayName { get; }

    internal Uri Endpoint { get; }

    internal string ConfigurationRoot { get; }

    internal IReadOnlyList<string> Scopes { get; }
}

internal static class McpOAuthOptions
{
    internal static ClientOAuthOptions Create(
        McpServerDefinition server,
        IConfiguration configuration,
        ITokenCache tokenCache,
        IMcpAuthorizationRedirect authorizationRedirect)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(tokenCache);
        ArgumentNullException.ThrowIfNull(authorizationRedirect);

        return new ClientOAuthOptions
        {
            ClientId = Required(configuration, server, "ClientId"),
            ClientSecret = Required(configuration, server, "ClientSecret"),
            RedirectUri = RequiredUri(configuration, server, "RedirectUri"),
            Scopes = server.Scopes,
            TokenCache = tokenCache,
            AuthorizationRedirectDelegate = authorizationRedirect.AuthorizeAsync,
        };
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

internal interface IMcpAuthorizationRedirect
{
    Task<string?> AuthorizeAsync(
        Uri authorizationUri,
        Uri redirectUri,
        CancellationToken cancellationToken);
}

internal sealed class RejectingMcpAuthorizationRedirect : IMcpAuthorizationRedirect
{
    internal static RejectingMcpAuthorizationRedirect Instance { get; } = new();

    private RejectingMcpAuthorizationRedirect()
    {
    }

    public Task<string?> AuthorizeAsync(
        Uri authorizationUri,
        Uri redirectUri,
        CancellationToken cancellationToken)
        => Task.FromException<string?>(new InvalidOperationException(
            "Interactive MCP authorization is not configured at an authenticated edge. "
            + "Use a durable pre-authorized token or explicitly enable the local loopback development adapter."));
}

internal sealed class LocalLoopbackMcpAuthorizationRedirect : IMcpAuthorizationRedirect
{
    public async Task<string?> AuthorizeAsync(
        Uri authorizationUri,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        ArgumentNullException.ThrowIfNull(redirectUri);

        if (!redirectUri.IsLoopback || redirectUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException(
                "The local MCP authorization adapter accepts only an HTTP loopback redirect URI.");
        }

        var expectedState = QueryValue(authorizationUri, "state")
            ?? throw new InvalidOperationException("The OAuth authorization URI contains no state value.");
        using var listener = new HttpListener();
        listener.Prefixes.Add($"{redirectUri.GetLeftPart(UriPartial.Authority)}/");
        listener.Start();

        Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri) { UseShellExecute = true });

        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            var callback = context.Request.Url;
            var validPath = callback is not null
                && string.Equals(callback.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal);
            var validState = callback is not null
                && string.Equals(QueryValue(callback, "state"), expectedState, StringComparison.Ordinal);

            if (!validPath || !validState)
            {
                await RespondAsync(context.Response, HttpStatusCode.BadRequest, "Invalid OAuth callback.", cancellationToken);
                continue;
            }

            var code = QueryValue(callback!, "code");
            await RespondAsync(
                context.Response,
                code is null ? HttpStatusCode.BadRequest : HttpStatusCode.OK,
                code is null
                    ? "DigitalBrain could not complete authorization. You can close this window."
                    : "DigitalBrain authorization completed. You can close this window.",
                cancellationToken);
            return code;
        }
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

    private static async Task RespondAsync(
        HttpListenerResponse response,
        HttpStatusCode status,
        string message,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload, cancellationToken);
        response.Close();
    }
}

internal static class McpRuntimeHosting
{
    internal const string AuthorizationModeKey = "DigitalBrain:Integrations:Mcp:AuthorizationMode";
    internal const string LocalLoopbackDevelopmentMode = "LocalLoopbackDevelopment";

    internal static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient(SdkMcpClient.HttpClientName);
        services.TryAddSingleton<IDurablePayloadProtector>(_ => new DurablePayloadProtector(
            configuration[DurablePayloadProtector.ConfigurationKey]
            ?? throw new InvalidOperationException(
                $"Missing shared durable state-protection key '{DurablePayloadProtector.ConfigurationKey}'.")));
        services.TryAddSingleton<IMcpAuthorizationRedirect>(_ =>
            string.Equals(
                configuration[AuthorizationModeKey],
                LocalLoopbackDevelopmentMode,
                StringComparison.Ordinal)
                ? new LocalLoopbackMcpAuthorizationRedirect()
                : RejectingMcpAuthorizationRedirect.Instance);
        services.TryAddSingleton<IMcpClientFactory, SdkMcpClientFactory>();
    }
}
