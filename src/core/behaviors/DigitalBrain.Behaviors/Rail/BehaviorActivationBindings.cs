using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

[GenerateSerializer]
[Alias("db.behavior.activation-binding")]
public sealed record BehaviorActivationBinding(
    [property: Id(0)] NeuronId TaskId,
    [property: Id(1)] NeuronId WorkerId,
    [property: Id(2)] BehaviorId BehaviorId,
    [property: Id(3)] BehaviorRevisionId Revision,
    [property: Id(4)] string ContractVersion,
    [property: Id(5)] string CaseId,
    [property: Id(6)] ProtectedPayloadReference ProtectedPayload);

public static class BehaviorActivationBindings
{
    public static BehaviorActivationBinding ForExistingTask(
        NeuronId taskId,
        NeuronId workerId,
        BehaviorId behaviorId,
        BehaviorRevisionId revision,
        string contractVersion,
        string caseId,
        ProtectedPayloadReference protectedPayload)
        => new(
            taskId,
            workerId,
            behaviorId,
            revision,
            contractVersion,
            caseId,
            protectedPayload);
}
