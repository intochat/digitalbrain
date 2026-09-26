namespace DigitalBrain.Sdk.Connectors;

[GenerateSerializer, Alias("connections.state")]
internal sealed record ConnectorsState
{
    [Id(0)] public Dictionary<string, ConnectorRecord> Connections { get; init; } = new(StringComparer.Ordinal);
}
