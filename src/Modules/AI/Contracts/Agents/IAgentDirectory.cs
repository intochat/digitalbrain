using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.AI.Agents;

// The agents other agents may delegate to. An agent is listed while its definition is Discoverable.
[Alias("ai.agent-directory"), Orleans.Metadata.DefaultGrainType("ai.agent-directory")]
public interface IAgentDirectory : INeuron
{
    [ReadOnly] Task<IReadOnlyList<AgentMetadata>> List();
    Task Publish(AgentMetadata agent);
    Task Withdraw(string agentId);
}

public static class AgentDirectoryGrains
{
    public const string Key = "agents";
}

[GenerateSerializer, Alias("ai.agent-directory-changed")]
public sealed record AgentDirectoryChanged([property: Id(0)] string AgentId) : Signal;
