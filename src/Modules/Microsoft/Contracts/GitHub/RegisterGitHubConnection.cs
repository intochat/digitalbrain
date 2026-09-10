using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Microsoft.GitHub;

/// <summary>Registers validated repository connection metadata.</summary>
[GenerateSerializer, Alias("db.github.register-git-hub-connection")]
public sealed record RegisterGitHubConnection(
    CommandId Id,
    [property: Id(0)] string ConnectionId,
    [property: Id(1)] long AppId,
    [property: Id(2)] long InstallationId,
    [property: Id(3)] long RepositoryId,
    [property: Id(4)] string RepositoryOwner,
    [property: Id(5)] string RepositoryName,
    [property: Id(6)] string Epoch) : Command(Id);
