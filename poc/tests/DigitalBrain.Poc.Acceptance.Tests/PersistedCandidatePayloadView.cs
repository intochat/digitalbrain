namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record PersistedCandidatePayloadView(
    string DeliveryId,
    string ProbeId,
    string ContractAlias,
    int SerializedByteCount);
