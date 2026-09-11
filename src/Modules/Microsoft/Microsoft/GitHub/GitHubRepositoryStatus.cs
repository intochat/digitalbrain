namespace DigitalBrain.Microsoft.GitHub;

public sealed record GitHubRepositoryStatus(GitHubSetupResult Setup, Uri? LoginUrl);
