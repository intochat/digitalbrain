using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace DigitalBrain.AI;

internal sealed record DirectOrchestrationIdentity(
    string KindName,
    string ExecutionEnvironmentName,
    string AggregatorName,
    Func<int, string> ManagerName);

internal sealed class DirectOrchestrationShape
{
    private readonly IReadOnlyList<Participant> _participants;
    private readonly Func<AIAgent[], Workflow> _buildWorkflow;
    private readonly IWorkflowExecutionEnvironment _executionEnvironment;

    private DirectOrchestrationShape(
        Type orchestrationType,
        IReadOnlyList<Participant> participants,
        DirectOrchestrationIdentity identity,
        Func<AIAgent[], Workflow> buildWorkflow,
        IWorkflowExecutionEnvironment executionEnvironment)
    {
        ArgumentNullException.ThrowIfNull(orchestrationType);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(buildWorkflow);
        ArgumentNullException.ThrowIfNull(executionEnvironment);

        _participants = participants;
        _buildWorkflow = buildWorkflow;
        _executionEnvironment = executionEnvironment;
        Definition = OrchestrationDefinition.Describe(
            orchestrationType,
            participants,
            identity);
    }

    internal OrchestrationDefinition Definition { get; }

    internal static Participant[] Snapshot(
        NeuronId orchestration,
        IReadOnlyList<Participant>? participants)
    {
        if (participants is null)
        {
            throw new InvalidOperationException("Participants returned null.");
        }

        var snapshot = participants.ToArray();

        if (snapshot.Length == 0)
        {
            throw new InvalidOperationException(
                $"AI orchestration '{orchestration}' requires at least one participant.");
        }

        if (snapshot.Any(participant => participant is null))
        {
            throw new InvalidOperationException(
                $"AI orchestration '{orchestration}' has a null participant.");
        }

        if (snapshot.Any(participant => participant.Id.Owner != orchestration.Owner))
        {
            throw new InvalidOperationException(
                $"Every participant in AI orchestration '{orchestration}' must belong to its owner.");
        }

        return snapshot;
    }

    internal static DirectOrchestrationShape CreateConcurrent(
        Type orchestrationType,
        IReadOnlyList<Participant> participants)
        => new(
            orchestrationType,
            participants,
            new(
                "concurrent",
                "in-process-concurrent",
                "concurrent-default",
                static _ => "none"),
            static agents => AgentWorkflowBuilder.BuildConcurrent(agents),
            InProcessExecution.Concurrent);

    internal static DirectOrchestrationShape CreateGroupChat(
        Type orchestrationType,
        IReadOnlyList<Participant> participants)
        => new(
            orchestrationType,
            participants,
            new(
                "group-chat",
                "in-process-lockstep",
                "none",
                static count => $"round-robin:{count}"),
            static agents => AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(team => new RoundRobinGroupChatManager(team)
                {
                    MaximumIterationCount = agents.Length,
                })
                .AddParticipants(agents)
                .Build(),
            InProcessExecution.Lockstep);

    internal AIAgent CreateAgent(
        IGrainFactory grains,
        TaskScheduler turnScheduler)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(turnScheduler);

        AIAgent[] agents =
        [
            .. _participants.Select(participant => participant.CreateAgent(grains, turnScheduler)),
        ];

        return _buildWorkflow(agents).AsAIAgent(
            id: Definition.HostId,
            name: Definition.HostName,
            executionEnvironment: _executionEnvironment);
    }
}
