namespace DigitalBrain.Poc.Runtime;

internal sealed record OutboxEntry(
    string DeliveryId,
    string ReceiptId,
    int OutputOrdinal,
    string Kind,
    string ContractAlias,
    string PayloadFormat,
    string PayloadBase64,
    string? OwnerId,
    string? CandidateFamily,
    string? ProducingRevision,
    CandidateModuleIdentity? ProducingModuleIdentity,
    string? TargetRevision,
    CandidateModuleIdentity? TargetModuleIdentity,
    string TargetNeuronType,
    bool Delivered,
    string? TargetScope = null);
