using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubRepositoryBindings
{
    internal const string ConfigurationRoot = "DigitalBrain:Microsoft:GitHub:Repositories";
    private readonly ConcurrentDictionary<string, GitHubRepositoryBinding> _bindings;
    internal GitHubRepositoryBindings(IEnumerable<GitHubRepositoryBinding> bindings)
    {
        _bindings = new(bindings.ToDictionary(static binding => binding.Id, StringComparer.Ordinal), StringComparer.Ordinal);
        if (_bindings.Count > 32 || _bindings.Values.Select(static b => b.EndpointId).Distinct(StringComparer.Ordinal).Count() != _bindings.Count)
        {
            throw new InvalidOperationException("Configure at most 32 GitHub bindings with unique endpoint and neuron identities.");
        }
    }

    public IReadOnlyCollection<GitHubRepositoryBinding> All => _bindings.Values.ToArray();

    internal void Add(GitHubRepositoryBinding binding)
    {
        if (_bindings.Count >= 256 && !_bindings.ContainsKey(binding.Id))
        {
            throw new GitHubUnavailableException("The GitHub connection capacity is full.");
        }

        _bindings.AddOrUpdate(binding.Id, binding, (_, previous) =>
        {
            if (previous.Revision == binding.Revision)
            {
                return previous;
            }

            previous.Revoke();
            return binding;
        });
    }

    internal GitHubRepositoryBinding? FindByRepository(string repoOwner, string repoName)
        => _bindings.Values.FirstOrDefault(binding =>
            string.Equals(binding.RepoOwner, repoOwner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(binding.RepoName, repoName, StringComparison.OrdinalIgnoreCase));
    public GitHubRepositoryBinding? Find(string id) => _bindings.GetValueOrDefault(id);
    public GitHubRepositoryBinding GetFor(string id)
        => Find(id) is { } binding
            ? binding
            : throw new GitHubAccessDeniedException("The GitHub repository neuron is not bound to a configured repository.");

    public static GitHubRepositoryBindings Read(IConfiguration configuration)
        => (configuration.GetSection(GitHubRepositoriesOptions.SectionName).Get<GitHubRepositoriesOptions>() ?? new()).CreateBindings();
}