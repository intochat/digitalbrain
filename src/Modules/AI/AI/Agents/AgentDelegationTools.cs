using System.ComponentModel;
using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Agents;

// Delegation reaches only listed agents, so a model cannot address an arbitrary agent grain.
internal sealed class AgentDelegationTools(IDigitalBrain brain) : IAgentToolFactory
{
    public const string SendToAgent = "send_to_agent";
    private const int ReplyLimit = 4000;

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> Send(
            [Description("The agent id from the capabilities listed for this request.")] string agentId,
            [Description("The full request, with every detail the agent needs; it does not see this conversation.")] string request,
            CancellationToken ct)
        {
            var listed = await brain.Get<IAgentDirectory>(AgentDirectoryGrains.Key).List().WaitAsync(ct);
            if (listed.All(agent => agent.Id != agentId))
            {
                return new { isError = true, message = $"'{agentId}' is not a listed agent. Available: {string.Join(", ", listed.Select(static agent => agent.Id))}." };
            }
            var reply = await brain.Get<IAgent>(agentId).GetResponse(request, ct);
            return new { agentId, reply = reply.Length > ReplyLimit ? reply[..ReplyLimit] + "…" : reply };
        }

        return [AIFunctionFactory.Create(Send, SendToAgent, "Delegate a task to an agent listed in the capabilities for this request and return its reply.")];
    }
}
