namespace DigitalBrain.Poc.ControlPlane;

public enum AttestationFailure
{
    None,
    Missing,
    MalformedAttestation,
    AttestationUnreadable,
    Signature,
    CandidateInventory,
    CandidateMetadataHash,
    CandidateMetadataUnavailable,
    SourceHash,
    SourceUnavailable,
    AssemblyHash,
    AssemblyUnavailable,
}
