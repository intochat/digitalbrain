namespace DigitalBrain.Poc.Runtime;

internal sealed record PersistedCandidatePayloadView(
    string DeliveryId,
    string ProbeId,
    string ContractAlias,
    int SerializedByteCount);
