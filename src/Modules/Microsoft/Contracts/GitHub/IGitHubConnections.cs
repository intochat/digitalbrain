using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.connections")]
public interface IGitHubConnections : INeuron
{
    /// <summary>Registers a validated connection.</summary>
    [Alias("register")]
    Task<Accepted<GitHubConnectionRecord>> Register(RegisterGitHubConnection command);

    /// <summary>Lists stored connections.</summary>
    [ReadOnly]
    [Alias("list")]
    Task<GitHubConnectionList> List();
}
