namespace DigitalBrain.Poc.ControlPlane;

public sealed record HostAuthorityDelegationPayload(
    string RunId,
    string ExpectedHeadPayloadHash,
    string ActiveSelectionHash);
