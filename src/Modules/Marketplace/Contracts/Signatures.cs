using DigitalBrain.Apps;

namespace DigitalBrain.Marketplace;

// Signatures are verified in require mode: an app without a valid Ed25519 signature is never listed.
public enum SignatureOutcome
{
    Valid = 0,
    Missing = 1,
    Invalid = 2,
}

[GenerateSerializer, Alias("marketplace.app-signature")]
public sealed record AppSignature
{
    [Id(0)] public required string KeyId { get; init; }
    [Id(1)] public required byte[] PublicKey { get; init; }
    [Id(2)] public required byte[] Signature { get; init; }
}

public interface IAppSignatureVerifier
{
    SignatureOutcome Verify(AppManifest manifest, AppSignature? signature);
}
