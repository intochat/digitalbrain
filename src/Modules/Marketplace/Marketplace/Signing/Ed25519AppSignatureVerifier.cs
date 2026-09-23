using System.Text;
using DigitalBrain.Apps;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace DigitalBrain.Marketplace;

// The signature gate. Every remote app carries an Ed25519 signature over the canonical app.json;
// the verifier runs in require mode, so a missing or invalid signature rejects the app.
public sealed class Ed25519AppSignatureVerifier : IAppSignatureVerifier
{
    public SignatureOutcome Verify(AppManifest manifest, AppSignature? signature)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (signature is null) { return SignatureOutcome.Missing; }
        if (signature.PublicKey.Length != Ed25519PublicKeyParameters.KeySize) { return SignatureOutcome.Invalid; }

        var payload = Ed25519Keys.Payload(manifest);
        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(signature.PublicKey));
            verifier.BlockUpdate(payload);
            return verifier.VerifySignature(signature.Signature) ? SignatureOutcome.Valid : SignatureOutcome.Invalid;
        }
        catch (ArgumentException)
        {
            return SignatureOutcome.Invalid;
        }
    }
}

public static class Ed25519Keys
{
    public static (byte[] PublicKey, byte[] Seed) Generate()
    {
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        return (privateKey.GeneratePublicKey().GetEncoded(), privateKey.GetEncoded());
    }

    public static byte[] Sign(byte[] seed, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(seed));
        signer.BlockUpdate(payload.ToArray());
        return signer.GenerateSignature();
    }

    public static byte[] Payload(AppManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Encoding.UTF8.GetBytes(AppManifestJson.Serialize(manifest));
    }
}
