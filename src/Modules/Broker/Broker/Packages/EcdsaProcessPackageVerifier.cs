using System.Security.Cryptography;
using DigitalBrain.Apps;
using DigitalBrain.Broker.Hosting;

namespace DigitalBrain.Broker.Packages;

// Verifies a process package before activation: package type, manifest kind, a publisher key the
// platform already trusts, a valid signature over the content hash, and no banned API anywhere in
// the shipped assemblies. Any failure leaves the app inert.
internal sealed class EcdsaProcessPackageVerifier(
    IReadOnlyDictionary<string, string> trustedPublisherKeys,
    BannedApiAnalyzer? bannedApiAnalyzer = null) : IProcessPackageVerifier
{
    private readonly BannedApiAnalyzer analyzer = bannedApiAnalyzer ?? new BannedApiAnalyzer();

    public ProcessPackageVerification Verify(ProcessAppPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var reasons = new List<string>();

        if (!string.Equals(package.PackageType, ProcessPackageContentHash.PackageTypeName, StringComparison.Ordinal))
        {
            reasons.Add($"Package type '{package.PackageType}' is not '{ProcessPackageContentHash.PackageTypeName}'.");
        }
        if (package.Manifest.Kind != AppKind.Process)
        {
            reasons.Add($"The manifest kind '{package.Manifest.Kind}' is not 'process'.");
        }

        if (package.Signature is null)
        {
            reasons.Add(
                $"'{package.Manifest.Id}' is unsigned; a process app is activated only after its signature verifies.");
        }
        else
        {
            VerifySignature(package, reasons);
        }

        foreach (var assembly in package.Assemblies)
        {
            foreach (var api in analyzer.Analyze(assembly.Bytes))
            {
                reasons.Add($"'{assembly.Name}' references banned API '{api}'.");
            }
        }

        return new ProcessPackageVerification { Allowed = reasons.Count == 0, Reasons = reasons };
    }

    private void VerifySignature(ProcessAppPackage package, List<string> reasons)
    {
        var signature = package.Signature!;
        if (!trustedPublisherKeys.TryGetValue(signature.Publisher, out var trustedKey)
            || !string.Equals(trustedKey, signature.PublicKey, StringComparison.Ordinal))
        {
            reasons.Add($"Publisher '{signature.Publisher}' is not a trusted process-app publisher.");
            return;
        }
        if (!HasValidSignature(trustedKey, package.ContentHash, signature.Signature))
        {
            reasons.Add($"The package signature for '{package.Manifest.Id}' does not match its content.");
        }
    }

    private static bool HasValidSignature(string publicKeyBase64, byte[] contentHash, string signatureBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return ecdsa.VerifyHash(contentHash, Convert.FromBase64String(signatureBase64));
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}