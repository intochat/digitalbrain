namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior.revision-approval")]
public sealed record BehaviorRevisionApproval(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Fingerprint,
    [property: Id(3)] NeuronId Approver,
    [property: Id(4)] DateTimeOffset ApprovedAt) : Synapse;
