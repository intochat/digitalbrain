using DigitalBrain.Kernel;
using DigitalBrain.Sdk;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubLogins(GitHubOAuthConfiguration configuration, TimeProvider clock) : BrowserLogins(LoginDefinition, clock)
{
    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "github", "GitHub", "GitHubIntegration", "/integrations/github/login", "/integrations/github/callback",
        "Connect the GitHub App to the requested repository. Credentials stay outside your conversation.");

    protected override Uri? PublicOrigin => configuration.IsConfigured ? configuration.PublicOrigin : null;
}
