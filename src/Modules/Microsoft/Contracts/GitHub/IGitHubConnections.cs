using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.connections")]
[Orleans.Metadata.DefaultGrainType("github.connections")]
public interface IGitHubConnections : INeuron
{
    Task<GitHubConnectionRecord> Register(RegisterGitHubConnection request);

    [ReadOnly]
    Task<GitHubConnectionList> List();
}