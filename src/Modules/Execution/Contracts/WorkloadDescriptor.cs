using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.workload")]
public abstract record WorkloadDescriptor;

[GenerateSerializer]
[Alias("db.workload.chat-turn.v1")]
public sealed record ChatTurnWorkload(
    [property: Id(0)] NeuronId ChatId,
    [property: Id(1)] Guid TurnId,
    [property: Id(2)] string UserText) : WorkloadDescriptor;

[GenerateSerializer]
[Alias("db.workload.automation.v1")]
public sealed record AutomationWorkload(
    [property: Id(0)] Guid AutomationId,
    [property: Id(1)] Guid RevisionId,
    [property: Id(2)] string GoalText) : WorkloadDescriptor;

[GenerateSerializer]
[Alias("db.workload.team.v1")]
public sealed record TeamWorkload(
    [property: Id(0)] string Goal,
    [property: Id(1)] IReadOnlyList<string> ParticipantNames) : WorkloadDescriptor;
