namespace DigitalBrain.Modules.Sdk.Mcp;

public sealed class McpServerDefinition
{
    public McpServerDefinition(
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

        }
        else if (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)
        {

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

    public string Key { get; }

    public string DisplayName { get; }

    public Uri Endpoint { get; }

    public string ConfigurationRoot { get; }

    public IReadOnlyList<string> Scopes { get; }

    public bool RequiresClientSecret { get; }

    public McpServerDefinition WithEndpoint(Uri endpoint)
        => new(Key, DisplayName, endpoint, ConfigurationRoot, Scopes, RequiresClientSecret);
}
