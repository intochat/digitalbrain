using DigitalBrain.Core.Distribution;

namespace DigitalBrain.Core;

// Legacy typed bundle metadata. Kept out of Core so current INO/automation contracts stay clean.
[GenerateSerializer]
[Alias("DigitalBrain.Core.NeuroPack")]
public record NeuroPack(
    [property: Id(0)] string Name,
    [property: Id(1)] string Version,
    [property: Id(2)] string OwnerId = "anonymous",
    [property: Id(3)] bool IsPrivate = false,
    [property: Id(4)] string Code = "",
    [property: Id(6)] string Description = "",
    // Trust chain: author's ECDSA public key (SPKI, base64) + signature over Name|Version|Hash(Code)|PubKey.
    // Empty = unsigned. Signed via PackSignatureVerifier.SignPack at publish, verified at install.
    [property: Id(7)] string AuthorPublicKeyBase64 = "",
    [property: Id(8)] string SignatureBase64 = "",
    [property: Id(9)] BundleManifest? Manifest = null
);
