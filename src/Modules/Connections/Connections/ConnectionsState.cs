namespace DigitalBrain.Connections;

[GenerateSerializer, Alias("connections.state")]
internal sealed record ConnectionsState
{
    [Id(0)] public Dictionary<string, ConnectionRecord> Connections { get; init; } = new(StringComparer.Ordinal);
}