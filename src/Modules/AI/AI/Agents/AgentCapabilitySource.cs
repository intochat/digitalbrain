using DigitalBrain.Contracts;
using DigitalBrain.Discovery;

namespace DigitalBrain.AI.Agents;

// Listed agents are searchable capabilities; finding one hands the turn the delegation tool.
internal sealed class AgentCapabilitySource(IDigitalBrain brain) : ICapabilitySource
{
    private IAgentDirectory Directory => brain.Get<IAgentDirectory>(AgentDirectoryGrains.Key);

    public async Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => [.. (await Directory.List().WaitAsync(cancellationToken).ConfigureAwait(false)).Select(static agent =>
            new CapabilityDocument(agent.Id, CapabilityKind.Agent, agent.DisplayName,
                string.Join(' ', [agent.Description, .. agent.Capabilities, .. agent.RoutingExamples]),
                Tools: [AgentDelegationTools.SendToAgent]))];

    public async Task Watch(Action changed, CancellationToken cancellationToken)
    {
        await using var changes = await brain.SubscribeAsync<AgentDirectoryChanged>(Directory, cancellationToken).ConfigureAwait(false);
        await foreach (var _ in changes.ReadAllAsync(cancellationToken).ConfigureAwait(false)) { changed(); }
    }
}
