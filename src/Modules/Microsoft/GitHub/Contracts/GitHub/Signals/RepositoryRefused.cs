using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.GitHub.Signals;

[GenerateSerializer, Alias("github.repository-refused")]
public sealed record RepositoryRefused(
    [property: Id(0)] string Reason) : Signal;