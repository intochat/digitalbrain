namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.git-hub-connection-list")]
public sealed record GitHubConnectionList(
    [property: Id(0)] IReadOnlyList<GitHubConnectionRecord> Connections);
