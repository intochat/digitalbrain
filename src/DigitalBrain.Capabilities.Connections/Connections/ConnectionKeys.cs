using DigitalBrain.Brains;

namespace DigitalBrain.Capabilities.Connections;

// A connection is keyed {brain}/connection/{connector}/{id}; the brain id carries no '/'.
public static class ConnectionKeys
{
    private const string Segment = "connection";
    public const string NeuronScheme = "neuron://";

    public static bool Is(string key) => key is not null && StripScheme(key).Split('/') is [_, Segment, _, _];

    public static string For(string brainId, string connectorId, string connectionId)
    {
        if (!BrainScope.IsValidId(brainId)) { throw new ArgumentException("A brain ID is required.", nameof(brainId)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return $"{brainId}/{Segment}/{Uri.EscapeDataString(connectorId)}/{Uri.EscapeDataString(connectionId)}";
    }

    public static string NeuronUri(string brainId, string connectorId, string connectionId)
        => NeuronScheme + For(brainId, connectorId, connectionId);

    public static string NeuronUri(string key)
    {
        var (brainId, connectorId, connectionId) = Parse(StripScheme(key));
        return NeuronUri(brainId, connectorId, connectionId);
    }

    public static NeuronId IdFor(string key)
    {
        var (brain, connector, _) = Parse(key);
        var normalized = StripScheme(key);
        return new NeuronId(new BrainId(brain), connector, normalized[(brain.Length + 1)..]);
    }

    public static (string BrainId, string ConnectorId, string ConnectionId) Parse(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var normalized = StripScheme(key);
        if (normalized.Split('/') is not [var brain, Segment, var connector, var connection] || !BrainScope.IsValidId(brain))
        { throw new ArgumentException("A connection grain key is required.", nameof(key)); }
        var (connectorId, connectionId) = (Uri.UnescapeDataString(connector), Uri.UnescapeDataString(connection));
        if (For(brain, connectorId, connectionId) != normalized) { throw new ArgumentException("A canonical connection key is required.", nameof(key)); }
        return (brain, connectorId, connectionId);
    }

    public static bool TryParse(string? key, out (string BrainId, string ConnectorId, string ConnectionId) parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key)) { return false; }
        try
        {
            parsed = Parse(key);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string StripScheme(string key)
        => key.StartsWith(NeuronScheme, StringComparison.Ordinal) ? key[NeuronScheme.Length..] : key;
}
