using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;
using Orleans.Runtime;

namespace DigitalBrain.CSharpExpert;

public interface ICodingAgentBackend
{
    Task<string> AskAsync(string agentId, string prompt, CancellationToken cancellationToken);

    Task<IReadOnlyList<EditRequest>> ProposeEditsAsync(string agentId, EditProposal proposal, CancellationToken cancellationToken);
}

[GrainType("csharp-expert.coding-agent")]
internal sealed class CodingAgentNeuron(ICodingAgentBackend backend) : Neuron, ICodingAgent
{
    private string AgentId => this.GetPrimaryKeyString();

    public Task<string> Ask(string prompt, CancellationToken cancellationToken = default)
        => backend.AskAsync(AgentId, prompt, cancellationToken);

    public Task<IReadOnlyList<EditRequest>> ProposeEdits(EditProposal proposal, CancellationToken cancellationToken = default)
        => backend.ProposeEditsAsync(AgentId, proposal, cancellationToken);
}
