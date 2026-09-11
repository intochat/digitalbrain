namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.repository-state")]
internal sealed record RepositoryState
{
    [Id(0)] public Dictionary<int, PullRequestSnapshot> PullRequests { get; init; } = [];
    [Id(1)] public string? BindingRevision { get; init; }
    [Id(2)] public bool Revoked { get; init; }
    [Id(3)] public DateTimeOffset? LastWebhookAt { get; init; }
}
