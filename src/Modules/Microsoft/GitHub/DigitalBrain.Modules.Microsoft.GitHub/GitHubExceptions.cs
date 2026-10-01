namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubUnavailableException(string message) : InvalidOperationException(message);

internal sealed class GitHubAccessDeniedException(string message) : InvalidOperationException(message);
