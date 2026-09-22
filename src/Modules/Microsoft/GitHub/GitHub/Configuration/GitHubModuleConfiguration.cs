using System.Globalization;
using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.GitHub;

public sealed class GitHubConfigurationContract() : ModuleConfigurationContract<GitHubModule, GitHubModuleOptions>("Repositories")
{
    protected override ModuleDefinition Compile(GitHubModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = new Dictionary<string, string?>();
        foreach (var (id, repo) in options.Repositories)
        {
            if (id.Length is 0 or > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            { throw new ArgumentException("Repository binding IDs must contain letters, numbers or hyphens.", nameof(options)); }
            var root = $"DigitalBrain:Microsoft:GitHub:Repositories:{id}";
            settings[$"{root}:AppId"] = repo.AppId.ToString(CultureInfo.InvariantCulture);
            settings[$"{root}:InstallationId"] = repo.InstallationId.ToString(CultureInfo.InvariantCulture);
            settings[$"{root}:RepositoryId"] = repo.RepositoryId.ToString(CultureInfo.InvariantCulture);
            settings[$"{root}:RepoOwner"] = repo.RepoOwner;
            settings[$"{root}:RepoName"] = repo.RepoName;
            settings[$"{root}:EndpointId"] = repo.EndpointId ?? id;
            settings[$"{root}:ApiHost"] = repo.ApiHost?.AbsoluteUri;
            settings[$"{root}:McpEndpoint"] = repo.McpEndpoint?.AbsoluteUri;
        }
        return new(typeof(GitHubModule), settings);
    }
}

public static class GitHubModuleConfiguration
{
    public static ModuleConfiguration<GitHubModule> WithGitHubRepositories(this ModuleConfiguration<GitHubModule> module,
        IReadOnlyDictionary<string, GitHubRepositoryDeclaration> repositories)
    {
        module.ConfigureOptions<GitHubModuleOptions>(o => o.Repositories = new(repositories), "Repositories");
        return module;
    }
}
