using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.mutation-approval")]
public sealed record SalesforceMutationApproval(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Fingerprint,
    [property: Id(3)] NeuronId Approver,
    [property: Id(4)] DateTimeOffset ApprovedAt) : Synapse;
