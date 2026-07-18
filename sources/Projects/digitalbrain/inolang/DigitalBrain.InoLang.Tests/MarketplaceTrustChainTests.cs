using DigitalBrain.Kernel.Runtime;
using FluentAssertions;
using Xunit;

namespace DigitalBrain.InoLang.Tests;

// Locks the cryptographic + metadata core every paid bundle depends on:
// the marketplace signs a bundle manifest, the installer verifies it, and the
// installer gates on @price/@license/@requires scanned from the .ino source.
public class MarketplaceTrustChainTests
{
    private const string Manifest =
        """{"bundleId":"acme/insurance-triage","version":"1.0.0"}""";

    [Fact]
    public void Signature_RoundTrips_ForUntamperedManifest()
    {
        var (privateKey, publicKey) = BundleSignatureVerifier.GenerateKeyPair();

        var signature = BundleSignatureVerifier.SignData(Manifest, privateKey);

        BundleSignatureVerifier.VerifyData(Manifest, signature, publicKey).Should().BeTrue();
    }

    [Fact]
    public void Signature_Rejects_TamperedManifest()
    {
        var (privateKey, publicKey) = BundleSignatureVerifier.GenerateKeyPair();
        var signature = BundleSignatureVerifier.SignData("""{"price":"19.99"}""", privateKey);

        var verified = BundleSignatureVerifier.VerifyData("""{"price":"0.00"}""", signature, publicKey);

        verified.Should().BeFalse("a tampered manifest must fail signature verification");
    }

    [Fact]
    public void Signature_Rejects_WrongPublicKey()
    {
        var (privateKey, _) = BundleSignatureVerifier.GenerateKeyPair();
        var (_, unrelatedPublicKey) = BundleSignatureVerifier.GenerateKeyPair();
        var signature = BundleSignatureVerifier.SignData(Manifest, privateKey);

        var verified = BundleSignatureVerifier.VerifyData(Manifest, signature, unrelatedPublicKey);

        verified.Should().BeFalse("a signature must not verify against an unrelated key pair");
    }

    [Fact]
    public void Scanner_DefaultsToFree_WhenNoCommercialTags()
    {
        var meta = InoMetadataScanner.Scan("neuron Acme.Free\n  \"no commercial tags here\"\n");

        meta.Price.Should().Be("free");
        meta.License.Should().Be("source-included");
        meta.Requires.Should().BeEmpty();
    }

    [Fact]
    public void Scanner_DetectsPremiumPriceLicenseAndRequires()
    {
        var source =
            "# @price: 19.99\n" +
            "# @license: commercial-eula\n" +
            "# @requires: DigitalBrain.Ai.Chat\n" +
            "neuron Acme.Insurance.Triage\n" +
            "  \"Classifies an inbound claim into a triage lane.\"\n";

        var meta = InoMetadataScanner.Scan(source);

        meta.Price.Should().Be("19.99");
        meta.License.Should().Be("commercial-eula");
        meta.Requires.Should().ContainSingle().Which.Should().Be("DigitalBrain.Ai.Chat");
    }
}
