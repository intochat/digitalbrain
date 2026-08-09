namespace DigitalBrain.Poc.ControlPlane;

public sealed record HostAuthorityDelegation(
    HostAuthorityDelegationPayload Payload,
    string Algorithm,
    string PublicKey,
    string Signature);
