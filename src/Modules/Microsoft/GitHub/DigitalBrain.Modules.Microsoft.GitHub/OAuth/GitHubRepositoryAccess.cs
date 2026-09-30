namespace DigitalBrain.Microsoft.GitHub;

internal sealed record GitHubRepositoryAccess(long AppId, long InstallationId, long RepositoryId, string RepositoryOwner, string RepositoryName);
