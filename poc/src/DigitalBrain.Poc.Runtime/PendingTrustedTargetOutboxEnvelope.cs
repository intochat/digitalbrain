namespace DigitalBrain.Poc.Runtime;

internal sealed record PendingTrustedTargetOutboxEnvelope(
    string DeliveryId,
    string Kind,
    string ContractAlias,
    string PayloadFormat,
    string PayloadBase64,
    string OwnerId,
    CandidateFamilyId Family,
    string ProducingRevision,
    CandidateModuleIdentity ProducingModuleIdentity,
    string TargetScope);
