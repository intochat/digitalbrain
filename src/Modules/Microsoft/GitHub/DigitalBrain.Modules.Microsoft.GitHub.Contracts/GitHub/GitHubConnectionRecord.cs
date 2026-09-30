namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.git-hub-connection-record")]
public sealed record GitHubConnectionRecord(
    [property: Id(0)] string Id,
    [property: Id(1)] long AppId,
    [property: Id(2)] long InstallationId,
    [property: Id(3)] long RepositoryId,
    [property: Id(4)] string RepositoryOwner,
    [property: Id(5)] string RepositoryName,
    [property: Id(6)] string Epoch);
