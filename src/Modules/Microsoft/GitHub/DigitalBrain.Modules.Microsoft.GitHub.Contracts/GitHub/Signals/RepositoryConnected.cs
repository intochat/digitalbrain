using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.GitHub.Signals;

[GenerateSerializer, Alias("github.repository-connected")]
public sealed record RepositoryConnected(
    [property: Id(0)] string BindingId,
    [property: Id(1)] long RepositoryId,
    [property: Id(2)] long InstallationId) : Signal;