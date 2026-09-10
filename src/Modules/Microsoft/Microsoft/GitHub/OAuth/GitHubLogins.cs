using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubLogins(GitHubOAuthConfiguration configuration) : BrowserLogins(LoginDefinition)
{
    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "github", "GitHub", "GitHubIntegration", "/integrations/github/login", "/integrations/github/callback",
        "Connect the GitHub App to the requested repository. Credentials stay outside your conversation.");

    protected override Uri? PublicOrigin => configuration.IsConfigured ? configuration.PublicOrigin : null;
}
