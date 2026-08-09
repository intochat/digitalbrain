namespace DigitalBrain.Poc.Runtime;

internal sealed record PendingOutboxEnvelope(
    string DeliveryId,
    string ContractAlias,
    string PayloadBase64,
    string OwnerId,
    CandidateFamilyId Family,
    string? ProducingRevision,
    CandidateModuleIdentity? ProducingModuleIdentity,
    string TargetRevision,
    CandidateModuleIdentity TargetModuleIdentity,
    string PayloadFormat,
    string TargetNeuronType);
