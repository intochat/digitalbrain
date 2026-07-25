using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace DigitalBrain.AI;

internal enum DirectOrchestrationKind
{
    Concurrent,
    GroupChat,
}

internal enum DirectExecutionEnvironment
{
    Concurrent,
    Lockstep,
}

internal enum DirectOrchestrationManager
{
    None,
    RoundRobin,
}

internal enum DirectOrchestrationAggregator
{
    None,
    ConcurrentDefault,
}

internal sealed record DirectOrchestrationIdentity(
    DirectOrchestrationKind Kind,
    DirectExecutionEnvironment ExecutionEnvironment,
    DirectOrchestrationManager Manager,
    DirectOrchestrationAggregator Aggregator)
{
    internal string KindName => Kind switch
    {
        DirectOrchestrationKind.Concurrent => "concurrent",
        DirectOrchestrationKind.GroupChat => "group-chat",
        _ => throw new InvalidOperationException($"Unknown direct orchestration kind '{Kind}'."),
    };

    internal string ExecutionEnvironmentName => ExecutionEnvironment switch
    {
        DirectExecutionEnvironment.Concurrent => "in-process-concurrent",
        DirectExecutionEnvironment.Lockstep => "in-process-lockstep",
        _ => throw new InvalidOperationException(
            $"Unknown direct orchestration execution environment '{ExecutionEnvironment}'."),
    };

    internal string ManagerName(int participantCount) => Manager switch
    {
        DirectOrchestrationManager.None => "none",
        DirectOrchestrationManager.RoundRobin => $"round-robin:{participantCount}",
        _ => throw new InvalidOperationException(
            $"Unknown direct orchestration manager '{Manager}'."),
    };

    internal string AggregatorName => Aggregator switch
    {
        DirectOrchestrationAggregator.None => "none",
        DirectOrchestrationAggregator.ConcurrentDefault => "concurrent-default",
        _ => throw new InvalidOperationException(
            $"Unknown direct orchestration aggregator '{Aggregator}'."),
    };
}

internal sealed class DirectOrchestrationShape
{
    private readonly IReadOnlyList<Participant> _participants;
    private readonly DirectOrchestrationIdentity _identity;

    private DirectOrchestrationShape(
        Type orchestrationType,
        IReadOnlyList<Participant> participants,
        DirectOrchestrationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(orchestrationType);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(identity);

        _participants = participants;
        _identity = identity;
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
                DirectOrchestrationKind.Concurrent,
                DirectExecutionEnvironment.Concurrent,
                DirectOrchestrationManager.None,
                DirectOrchestrationAggregator.ConcurrentDefault));

    internal static DirectOrchestrationShape CreateGroupChat(
        Type orchestrationType,
        IReadOnlyList<Participant> participants)
        => new(
            orchestrationType,
            participants,
            new(
                DirectOrchestrationKind.GroupChat,
                DirectExecutionEnvironment.Lockstep,
                DirectOrchestrationManager.RoundRobin,
                DirectOrchestrationAggregator.None));

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
        var workflow = _identity.Kind switch
        {
            DirectOrchestrationKind.Concurrent => AgentWorkflowBuilder.BuildConcurrent(agents),
            DirectOrchestrationKind.GroupChat => AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(team => new RoundRobinGroupChatManager(team)
                {
                    MaximumIterationCount = agents.Length,
                })
                .AddParticipants(agents)
                .Build(),
            _ => throw new InvalidOperationException(
                $"Unknown direct orchestration kind '{_identity.Kind}'."),
        };
        IWorkflowExecutionEnvironment environment = _identity.ExecutionEnvironment switch
        {
            DirectExecutionEnvironment.Concurrent => InProcessExecution.Concurrent,
            DirectExecutionEnvironment.Lockstep => InProcessExecution.Lockstep,
            _ => throw new InvalidOperationException(
                $"Unknown direct orchestration execution environment '{_identity.ExecutionEnvironment}'."),
        };

        return workflow.AsAIAgent(
            id: Definition.HostId,
            name: Definition.HostName,
            description: null,
            executionEnvironment: environment,
            includeExceptionDetails: false,
            includeWorkflowOutputsInResponse: false);
    }
}
