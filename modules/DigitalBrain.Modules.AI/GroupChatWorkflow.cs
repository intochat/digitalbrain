using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace DigitalBrain.AI;

internal static class GroupChatWorkflow
{
    internal static Workflow Create(IReadOnlyList<AIAgent> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);

        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(team => new RoundRobinGroupChatManager(team)
            {
                MaximumIterationCount = participants.Count,
            })
            .AddParticipants([.. participants])
            .Build();
    }
}
