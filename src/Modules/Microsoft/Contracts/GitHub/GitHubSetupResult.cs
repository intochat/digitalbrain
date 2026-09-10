namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.git-hub-setup-result")]
public sealed record GitHubSetupResult(
    [property: Id(0)] string RepositoryUrl,
    [property: Id(1)] string State,
    [property: Id(2)] string? BindingId,
    [property: Id(3)] IReadOnlyList<GitHubCheckRequirement> RequiredChecks,
    [property: Id(4)] string Detail,
    [property: Id(5)] string? WebhookUrl = null);
