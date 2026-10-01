namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.connections-state")]
internal sealed record GitHubConnectionsState([property: Id(0)] IReadOnlyList<GitHubConnectionRecord> Connections);
