using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.AI.Agents;

[GenerateSerializer, Alias("ai.agent-directory-state")]
internal sealed record AgentDirectoryState
{
    [Id(0)] public Dictionary<string, AgentMetadata> Agents { get; init; } = new(StringComparer.Ordinal);
}

[GrainType("ai.agent-directory")]
internal sealed class AgentDirectoryNeuron(
    [PersistentState("directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AgentDirectoryState> store)
    : Neuron<AgentDirectoryState>(store), IAgentDirectory
{
    public Task<IReadOnlyList<AgentMetadata>> List()
        => Task.FromResult<IReadOnlyList<AgentMetadata>>([.. Snapshot.Agents.Values.OrderBy(static agent => agent.Id, StringComparer.Ordinal)]);

    public Task Publish(AgentMetadata agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.Id);
        if (Snapshot.Agents.TryGetValue(agent.Id, out var listed) && Same(listed, agent)) { return Task.CompletedTask; }
        return Save(Snapshot with { Agents = new(Snapshot.Agents, StringComparer.Ordinal) { [agent.Id] = agent } }, new AgentDirectoryChanged(agent.Id));
    }

    public Task Withdraw(string agentId)
    {
        if (!Snapshot.Agents.ContainsKey(agentId)) { return Task.CompletedTask; }
        var agents = new Dictionary<string, AgentMetadata>(Snapshot.Agents, StringComparer.Ordinal);
        agents.Remove(agentId);
        return Save(Snapshot with { Agents = agents }, new AgentDirectoryChanged(agentId));
    }

    private static bool Same(AgentMetadata left, AgentMetadata right)
        => left.DisplayName == right.DisplayName && left.Description == right.Description
            && left.Capabilities.SequenceEqual(right.Capabilities) && left.RoutingExamples.SequenceEqual(right.RoutingExamples);
}
