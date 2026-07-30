using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.policy")]
public sealed record TaskPolicy(
    [property: Id(0)] int MaximumAttempts,
    [property: Id(1)] TimeSpan RetryDelay,
    [property: Id(2)] DateTimeOffset? Deadline);

[GenerateSerializer]
[Alias("tasks.behavior-activation")]
public sealed record BehaviorTaskActivation
{
    public BehaviorTaskActivation(
        BehaviorId behaviorId,
        BehaviorRevisionId revision,
        string contractVersion,
        string caseId,
        ProtectedPayloadReference protectedPayload)
    {
        BehaviorId = behaviorId;
        Revision = revision;
        ContractVersion = contractVersion;
        CaseId = caseId;
        ProtectedPayload = protectedPayload;
    }

    [Id(0)]
    public BehaviorId BehaviorId { get; init; }

    [Id(1)]
    public BehaviorRevisionId Revision { get; init; }

    [Id(2)]
    public string ContractVersion { get; init; }

    [Id(3)]
    public string CaseId { get; init; }

    [Id(4)]
    public ProtectedPayloadReference ProtectedPayload { get; init; }
}

[GenerateSerializer]
[Alias("tasks.start")]
public sealed record StartTask(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] Goal Goal,
    [property: Id(2)] NeuronId Worker,
    [property: Id(3)] TaskPolicy Policy,
    [property: Id(4)] NeuronId? RetryOf = null,
    [property: Id(5)] BehaviorTaskActivation? Activation = null) : RequestSynapse<TaskSnapshot>;

[GenerateSerializer]
[Alias("tasks.cancel")]
public sealed record CancelTask(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long ExpectedRevision);
