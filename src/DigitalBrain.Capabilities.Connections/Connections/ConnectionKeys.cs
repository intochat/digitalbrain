using DigitalBrain.Brains;

namespace DigitalBrain.Capabilities.Connections;

internal readonly record struct ConnectionAddress(BrainId Brain, string ConnectorId, string ConnectionId)
{
    private const string Segment = "connection";

    public string Key => $"{Brain.Value}/{Segment}/{Uri.EscapeDataString(ConnectorId)}/{Uri.EscapeDataString(ConnectionId)}";

    public string UriString => ConnectionKeys.NeuronScheme + Key;

    public NeuronId ToNeuronId() => new(Brain, ConnectorId, $"{Segment}/{Uri.EscapeDataString(ConnectorId)}/{Uri.EscapeDataString(ConnectionId)}");

    public (string Brain, string Connector, string Connection) ToTuple() => (Brain.Value, ConnectorId, ConnectionId);

    public static ConnectionAddress Create(string brainId, string connectorId, string connectionId)
    {
        if (!BrainScope.IsValidId(brainId)) { throw new ArgumentException("A brain ID is required.", nameof(brainId)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return new ConnectionAddress(new BrainId(brainId), connectorId, connectionId);
    }

    public static ConnectionAddress Parse(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var normalized = StripScheme(key);
        if (normalized.Split('/') is not [var brain, Segment, var connector, var connection] || !BrainScope.IsValidId(brain))
        {
            throw new ArgumentException("A connection grain key is required.", nameof(key));
        }

        var (connectorId, connectionId) = (Uri.UnescapeDataString(connector), Uri.UnescapeDataString(connection));
        var address = Create(brain, connectorId, connectionId);
        if (address.Key != normalized)
        {
            throw new ArgumentException("A canonical connection key is required.", nameof(key));
        }

        return address;
    }

    public static bool TryParse(string? key, out ConnectionAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(key)) { return false; }
        try
        {
            address = Parse(key);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string StripScheme(string key)
        => key.StartsWith(ConnectionKeys.NeuronScheme, StringComparison.Ordinal) ? key[ConnectionKeys.NeuronScheme.Length..] : key;
}

public static class ConnectionKeys
{
    private const string Segment = "connection";
    public const string NeuronScheme = "neuron://";

    public static bool Is(string key) => key is not null && ConnectionAddress.StripScheme(key).Split('/') is [_, Segment, _, _];

    public static string For(string brainId, string connectorId, string connectionId)
        => ConnectionAddress.Create(brainId, connectorId, connectionId).Key;

    public static string NeuronUri(string brainId, string connectorId, string connectionId)
        => ConnectionAddress.Create(brainId, connectorId, connectionId).UriString;

    public static string NeuronUri(string key)
        => ConnectionAddress.Parse(key).UriString;

    public static NeuronId IdFor(string key)
        => ConnectionAddress.Parse(key).ToNeuronId();

    public static (string BrainId, string ConnectorId, string ConnectionId) Parse(string key)
        => ConnectionAddress.Parse(key).ToTuple();

    public static bool TryParse(string? key, out (string BrainId, string ConnectorId, string ConnectionId) parsed)
    {
        if (ConnectionAddress.TryParse(key, out var address))
        {
            parsed = address.ToTuple();
            return true;
        }

        parsed = default;
        return false;
    }
}
