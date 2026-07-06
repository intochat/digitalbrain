using DigitalBrain.Core.Distribution;

namespace DigitalBrain.Core;

// A NeuroPack is the distributable unit: metadata + code + ownership + monetization info.
// This enables private marketplace + commissions without keeping pack identity in Core.
[GenerateSerializer]
public record NeuroPack(
    [property: Id(0)] string Name,
    [property: Id(1)] string Version,
    [property: Id(2)] string OwnerId = "anonymous",
    [property: Id(3)] bool IsPrivate = false,
    [property: Id(4)] double CommissionRate = 0.10,
    [property: Id(5)] string Code = "",
    [property: Id(6)] string Description = "",
    // Trust chain: author's ECDSA public key (SPKI, base64) + signature over Name|Version|Hash(Code)|PubKey.
    // Empty = unsigned. Signed via PackSignatureVerifier.SignPack at publish, verified at install.
    [property: Id(7)] string AuthorPublicKeyBase64 = "",
    [property: Id(8)] string SignatureBase64 = "",
    // Economics: price in the marketplace currency. 0 = free. Premium (>0) packs require a license at install.
    [property: Id(9)] decimal Price = 0m,
    [property: Id(10)] BundleManifest? Manifest = null
);
