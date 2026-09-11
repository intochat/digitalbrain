using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.connections")]
public interface IGitHubConnections : INeuron
{
    [Alias("register")]
    Task<Accepted<GitHubConnectionRecord>> Register(RegisterGitHubConnection command);

    [ReadOnly]
    [Alias("list")]
    Task<GitHubConnectionList> List();
}
