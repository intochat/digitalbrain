using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.connect-repository")]
public sealed record ConnectRepository(
    CommandId Id,
    [property: Id(0)] long AppId,
    [property: Id(1)] long InstallationId,
    [property: Id(2)] long RepositoryId,
    [property: Id(3)] string RepositoryOwner,
    [property: Id(4)] string RepositoryName) : Command(Id);
