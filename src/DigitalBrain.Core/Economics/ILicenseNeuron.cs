namespace DigitalBrain.Core.Economics;

// ECDSA-signed software licenses for premium marketplace packs. Reuses PackSignatureVerifier (ECDSA-nistP256).
// Harvested from digitalbrain's LicenseNeuron (token shape), re-homed onto MAIN's Neuron/journal model — the
// entitlement source of truth is the journal (LicenseGranted), not a Postgres DB.
[Alias("DigitalBrain.Core.Economics.ILicenseNeuron")]
public interface ILicenseNeuron : INeuron
{
    // Issue a signed, portable license token for (bundleId, userId) and record the grant. Returns the token.
    [Alias("IssueLicenseAsync")]
    Task<string> IssueLicenseAsync(string bundleId, string userId);

    // Verify a portable token: signature (against the license server's own key) + payload match.
    [Alias("VerifyLicenseAsync")]
    Task<bool> VerifyLicenseAsync(string licenseToken, string bundleId, string userId);

    // In-cluster entitlement check: has a license been granted to userId for bundleId?
    [Alias("HasLicenseAsync")]
    Task<bool> HasLicenseAsync(string bundleId, string userId);

    // Overloads using the Core UserId contract (preferred for new code; strings kept for compat).
    [Alias("IssueLicenseForUserIdAsync")]
    Task<string> IssueLicenseAsync(string bundleId, UserId userId) => IssueLicenseAsync(bundleId, userId.Value);
    [Alias("VerifyLicenseForUserIdAsync")]
    Task<bool> VerifyLicenseAsync(string licenseToken, string bundleId, UserId userId) => VerifyLicenseAsync(licenseToken, bundleId, userId.Value);
    [Alias("HasLicenseForUserIdAsync")]
    Task<bool> HasLicenseAsync(string bundleId, UserId userId) => HasLicenseAsync(bundleId, userId.Value);
}

// Journal record of an issued license — the entitlement source of truth for install gating.
[GenerateSerializer]
[Alias("DigitalBrain.Core.Economics.LicenseGranted")]
public record LicenseGranted(string BundleId, string UserId, string Token)
    : Synapse(nameof(LicenseGranted), DateTimeOffset.UtcNow)
{
    public LicenseGranted(string bundleId, UserId userId, string token) : this(bundleId, userId.Value, token) { }
}

// The license server's persistent ECDSA key pair (journal-persisted). Production should source the key from
// Key Vault rather than the journal; this is the dev/self-contained path.
[GenerateSerializer]
[Alias("DigitalBrain.Core.Economics.LicenseKeyPair")]
public record LicenseKeyPair(string PrivateKeyBase64, string PublicKeyBase64)
    : Synapse(nameof(LicenseKeyPair), DateTimeOffset.UtcNow);
