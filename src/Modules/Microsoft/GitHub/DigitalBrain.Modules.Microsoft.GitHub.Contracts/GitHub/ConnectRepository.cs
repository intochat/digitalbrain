namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("github.connect-repository")]
public sealed record ConnectRepository(
    [property: Id(0)] long AppId,
    [property: Id(1)] long InstallationId,
    [property: Id(2)] long RepositoryId,
    [property: Id(3)] string RepositoryOwner,
    [property: Id(4)] string RepositoryName);