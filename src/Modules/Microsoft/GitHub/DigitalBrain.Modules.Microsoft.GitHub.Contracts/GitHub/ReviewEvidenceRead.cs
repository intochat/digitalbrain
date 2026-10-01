namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.review-evidence-read")]
public sealed record ReviewEvidenceRead(
    [property: Id(0)] PullRequestSnapshot Current,
    [property: Id(1)] GitHubReviewEvidence? Evidence,
    [property: Id(2)] string? Detail = null);
