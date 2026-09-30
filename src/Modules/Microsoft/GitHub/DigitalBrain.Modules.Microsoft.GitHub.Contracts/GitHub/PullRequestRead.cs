namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.pull-request-read")]
public sealed record PullRequestRead(
    [property: Id(0)] PullRequestSnapshot Snapshot);
