namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.read-pull-request")]
public sealed record ReadPullRequest(
    [property: Id(0)] int Number);
