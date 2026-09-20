using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.GitHub.Signals;

[GenerateSerializer, Alias("github.repository-access-revoked")]
public sealed record RepositoryAccessRevoked(
    [property: Id(0)] string BindingId) : Signal;