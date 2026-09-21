using System.Globalization;
using DigitalBrain.Core;

namespace DigitalBrain.Microsoft;

public sealed class MicrosoftConfigurationContract() : ModuleConfigurationContract<MicrosoftModule, MicrosoftModuleOptions>(
    "AspireProjectPath", "AspireApplicationName", "Repositories")
{
    protected override ModuleDefinition Compile(MicrosoftModuleOptions options)
    {
        var settings = new Dictionary<string, string?>(MicrosoftModule.Define(options).Configuration);
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
        return new(typeof(MicrosoftModule), settings);
    }
}

public static class MicrosoftModuleConfiguration
{
    public static ModuleConfiguration<MicrosoftModule> WithAspire(this ModuleConfiguration<MicrosoftModule> module, string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        module.ConfigureOptions<MicrosoftModuleOptions>(o => o.AspireProjectPath = Path.GetFullPath(projectPath), "AspireProjectPath");
        return module;
    }
    public static ModuleConfiguration<MicrosoftModule> WithoutAspire(this ModuleConfiguration<MicrosoftModule> module)
    {
        module.ConfigureOptions<MicrosoftModuleOptions>(o => o.AspireProjectPath = null, "AspireProjectPath");
        return module;
    }
    public static ModuleConfiguration<MicrosoftModule> WithGitHubRepositories(this ModuleConfiguration<MicrosoftModule> module,
        IReadOnlyDictionary<string, GitHubRepositoryDeclaration> repositories)
    {
        module.ConfigureOptions<MicrosoftModuleOptions>(o => o.Repositories = new(repositories), "Repositories");
        return module;
    }
}