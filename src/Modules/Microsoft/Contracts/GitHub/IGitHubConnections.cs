using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.connections")]
public interface IGitHubConnections : INeuron
{
    [Alias("register")]
    [NeuronTool]
    Task<Accepted<GitHubConnectionRecord>> Register(RegisterGitHubConnection command);

    [ReadOnly]
    [Alias("list")]
    [NeuronTool(IsReadOnly = true)]
    Task<GitHubConnectionList> List();
}
