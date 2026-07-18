using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Kernel.Runtime;

/// <summary>
/// Handles cryptographic signing and verification of InoLang bundle manifests and license grants using ECDSA (nistP256) and SHA256.
/// </summary>
public static class BundleSignatureVerifier
{
    /// <summary>
    /// Generates a new ECDSA key pair (nistP256) in PKCS8 private and SubjectPublicKeyInfo public DER format.
    /// </summary>
    public static (byte[] PrivateKey, byte[] PublicKey) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKey(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    /// <summary>
    /// Signs a UTF-8 text string using a PKCS8 private key DER.
    /// </summary>
    public static byte[] SignData(string text, byte[] privateKeyDer)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateKeyDer, out _);
        var data = Encoding.UTF8.GetBytes(text);
        return ecdsa.SignData(data, HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Verifies that a UTF-8 text string matches the signature using a SubjectPublicKeyInfo public key DER.
    /// </summary>
    public static bool VerifyData(string text, byte[] signatureBytes, byte[] publicKeyDer)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeyDer, out _);
            var data = Encoding.UTF8.GetBytes(text);
            return ecdsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }
}
