using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubOAuthConfiguration(IOptions<GitHubAppOptions> options)
{
    internal GitHubOAuthConfiguration(IConfiguration configuration)
        : this(Options.Create(configuration.GetSection(GitHubAppOptions.SectionName).Get<GitHubAppOptions>() ?? new())) { }

    internal const string Root = "DigitalBrain:Microsoft:GitHub:App";
    internal string ClientId => options.Value.ClientId ?? "";
    internal string ClientSecret => options.Value.ClientSecret ?? "";
    internal long AppId => options.Value.ParsedAppId;
    internal string Slug => options.Value.Slug ?? "";
    internal Uri? PublicOrigin => Uri.TryCreate(options.Value.PublicOrigin, UriKind.Absolute, out var origin)
        && (origin.Scheme == "https" || origin.Scheme == "http" && origin.IsLoopback)
        && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0 && origin.UserInfo.Length == 0
        ? origin : null;
    internal bool IsConfigured => AppId > 0 && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret) && PublicOrigin is not null;
}
