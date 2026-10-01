namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.pull-requests-read")]
public sealed record PullRequestsRead(
    [property: Id(0)] IReadOnlyList<PullRequestSnapshot> Snapshots,
    [property: Id(1)] bool Available);
