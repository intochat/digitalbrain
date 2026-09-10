namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.read-review-evidence")]
public sealed record ReadReviewEvidence(
    [property: Id(0)] PullRequestSnapshot Expected);
