using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Security;

namespace DigitalBrain.Behaviors;

internal sealed class BehaviorArtifactTrust(IDurablePayloadProtector protector) : IBehaviorArtifactTrust
{
    public const string Purpose = "DigitalBrain.Behaviors.ArtifactSignature/v1";

    public byte[] Sign(string artifactHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
        return protector.Protect(Purpose, Encoding.UTF8.GetBytes(artifactHash));
    }

    public void Verify(string artifactHash, ReadOnlySpan<byte> signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
        if (signature.IsEmpty)
        {
            throw new BehaviorHostException("unsigned-artifact");
        }

        try
        {
            var recovered = protector.Unprotect(Purpose, signature);
            var expected = Encoding.UTF8.GetBytes(artifactHash);
            if (!CryptographicOperations.FixedTimeEquals(recovered, expected))
            {
                CryptographicOperations.ZeroMemory(recovered);
                throw new BehaviorHostException("signature-hash-mismatch");
            }

            CryptographicOperations.ZeroMemory(recovered);
        }
        catch (BehaviorHostException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            throw new BehaviorHostException("invalid-signature", exception);
        }
    }
}
