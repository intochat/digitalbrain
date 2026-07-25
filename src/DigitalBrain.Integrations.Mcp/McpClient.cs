using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;

namespace DigitalBrain.Integrations.Mcp;

internal sealed class McpServerDefinition
{
    internal McpServerDefinition(
        string key,
        string displayName,
        Uri endpoint,
        string configurationRoot,
        IReadOnlyList<string> scopes,
        bool requiresClientSecret = true)
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
        RequiresClientSecret = requiresClientSecret;
    }

    internal string Key { get; }

    internal string DisplayName { get; }

    internal Uri Endpoint { get; }

    internal string ConfigurationRoot { get; }

    internal IReadOnlyList<string> Scopes { get; }

    internal bool RequiresClientSecret { get; }
}

internal sealed class McpRuntime(
    IConfiguration configuration,
    IHttpClientFactory httpClients,
    IDurablePayloadProtector protector)
{
    internal const string HttpClientName = "DigitalBrain.Integrations.Mcp";

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The official MCP client takes ownership of its transport and disposes it with the session.")]
    internal async ValueTask<T> RunAsync<T>(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        Func<ModelContextProtocol.Client.McpClient, CancellationToken, ValueTask<T>> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        ArgumentNullException.ThrowIfNull(callback);

        var tokens = new DurableMcpTokenCache(
            tokenState,
            commit,
            protector,
            $"mcp/oauth/{server.Key}/{durableIdentity}");
        var authorization = McpOAuthOptions.Create(server, configuration, tokens);
        using var httpClient = httpClients.CreateClient(HttpClientName);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = server.Endpoint,
                Name = server.DisplayName,
                OAuth = authorization,
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: false);
        await using var client = await ModelContextProtocol.Client.McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        return await callback(client, cancellationToken);
    }

    internal static JsonElement RequireStructuredContent(
        CallToolResult result,
        McpServerDefinition server,
        string toolName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (result.IsError is true)
        {
            throw new InvalidOperationException(
                $"{server.DisplayName} MCP tool '{toolName}' reported an error.");
        }

        return result.StructuredContent?.Clone()
            ?? throw new InvalidOperationException(
                $"{server.DisplayName} MCP tool '{toolName}' returned no structured content.");
    }
}
