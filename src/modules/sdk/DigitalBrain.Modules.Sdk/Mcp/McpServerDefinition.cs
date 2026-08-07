namespace DigitalBrain.Modules.Sdk.Mcp;

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

        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("An MCP server endpoint must be an absolute URI.", nameof(endpoint));
        }

        if (endpoint.Scheme == Uri.UriSchemeHttps)
        {
            // Production endpoints are HTTPS.
        }
        else if (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)
        {
            // Loopback HTTP is reserved for test-owned fake providers.
        }
        else
        {
            throw new ArgumentException(
                "An MCP server endpoint must be HTTPS, or HTTP on a loopback address for test hosts.",
                nameof(endpoint));
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

    internal McpServerDefinition WithEndpoint(Uri endpoint)
        => new(Key, DisplayName, endpoint, ConfigurationRoot, Scopes, RequiresClientSecret);
}
