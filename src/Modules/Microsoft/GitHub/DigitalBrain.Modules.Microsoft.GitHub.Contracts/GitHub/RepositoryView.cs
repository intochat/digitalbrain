namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.repository-view")]
public sealed record RepositoryView(
    [property: Id(0)] string BindingId,
    [property: Id(1)] bool Connected,
    [property: Id(2)] bool Revoked,
    [property: Id(3)] IReadOnlyList<PullRequestSnapshot> PullRequests,
    [property: Id(4)] DateTimeOffset? LastWebhookAt);
