using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.GitHub.Signals;

[GenerateSerializer, Alias("github.connection-registered")]
public sealed record GitHubConnectionRegistered(
    [property: Id(0)] GitHubConnectionRecord Record) : Signal;