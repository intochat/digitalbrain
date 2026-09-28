using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Agents;

// A module contributes agent tools by registering a factory; the context names the trusted scope,
// run and tool call the tools act for.
public sealed record AgentToolContext(string ScopeId, string RunId, string CallId);

public interface IAgentToolFactory
{
    IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context);
}
