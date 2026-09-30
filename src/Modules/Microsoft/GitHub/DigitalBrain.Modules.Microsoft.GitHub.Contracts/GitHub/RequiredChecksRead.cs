namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.required-checks-read")]
public sealed record RequiredChecksRead(
    [property: Id(0)] IReadOnlyList<GitHubCheckRequirement> Checks,
    [property: Id(1)] bool Complete,
    [property: Id(2)] string? Detail = null);
