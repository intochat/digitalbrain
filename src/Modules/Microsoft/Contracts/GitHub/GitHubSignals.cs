namespace DigitalBrain.Microsoft.GitHub;

public static class GitHubSignals
{
    public const string RepositoryEvent = nameof(RepositoryEvent);
    public const string PullRequestChanged = nameof(PullRequestChanged);
    public const string RepositoryAccessRevoked = nameof(RepositoryAccessRevoked);
    public const string RepositoryConnected = nameof(RepositoryConnected);
    public const string RepositoryRefreshRequested = nameof(RepositoryRefreshRequested);
    public const string GitHubConnectionRegistered = nameof(GitHubConnectionRegistered);
}

[GenerateSerializer, Alias("db.github.repository-event")]
public sealed record RepositoryEvent([property: Id(0)] string DeliveryId, [property: Id(1)] string Event, [property: Id(2)] string? Action, [property: Id(3)] int? Number, [property: Id(4)] string BodyHash);

[GenerateSerializer, Alias("db.github.pull-request-changed")]
public sealed record PullRequestChanged([property: Id(0)] int Number, [property: Id(1)] string Version, [property: Id(2)] string SubjectKey);

[GenerateSerializer, Alias("db.github.repository-access-revoked")]
public sealed record RepositoryAccessRevoked([property: Id(0)] string BindingId);
