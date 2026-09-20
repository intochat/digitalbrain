using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.GitHub.Signals;

[GenerateSerializer, Alias("github.pull-request-changed")]
public sealed record PullRequestChanged(
    [property: Id(0)] int Number,
    [property: Id(1)] string Version,
    [property: Id(2)] string SubjectKey) : Signal;