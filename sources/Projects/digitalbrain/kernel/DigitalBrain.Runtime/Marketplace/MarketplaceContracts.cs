using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Marketplace
{
    // ==========================================
    // DATA CONTRACTS
    // ==========================================

    [GenerateSerializer]
    public sealed record BundleInfo(
        [property: Id(0)] string BundleId,
        [property: Id(1)] string Version,
        [property: Id(2)] string ManifestJson,
        [property: Id(3)] byte[] Signature,
        [property: Id(4)] string Price,
        [property: Id(5)] string License,
        [property: Id(6)] byte[]? ZipBytes = null);

    [GenerateSerializer]
    public sealed record PurchaseRow(
        [property: Id(0)] string PurchaseId,
        [property: Id(1)] string BundleId,
        [property: Id(2)] string UserId,
        [property: Id(3)] DateTimeOffset Timestamp);

    [GenerateSerializer]
    public sealed record LicenseRow(
        [property: Id(0)] string LicenseToken,
        [property: Id(1)] string BundleId,
        [property: Id(2)] string UserId,
        [property: Id(3)] DateTimeOffset IssuedAtUtc);

    // ==========================================
    // MARKETPLACE SYNAPSES
    // ==========================================

    [GenerateSerializer]
    public sealed record GetBundlesQuery : Synapse;

    [GenerateSerializer]
    public sealed record GetBundlesResponse([property: Id(1)] List<BundleInfo> Bundles
) : Synapse;

    [GenerateSerializer]
    public sealed record BuyBundleCommand([property: Id(1)] string BundleId,
    [property: Id(2)] string UserId
) : Synapse;

    // A premium buy returns a Stripe Checkout URL/session and an empty LicenseToken — the
    // license is minted only after the webhook confirms payment. A free buy short-circuits
    // and returns the LicenseToken immediately (no payment due).
    [GenerateSerializer]
    public sealed record BuyBundleResponse([property: Id(1)] bool Success,
    [property: Id(2)] string LicenseToken,
    [property: Id(3)] string ErrorMessage,
    [property: Id(4)] string CheckoutUrl = "",
    [property: Id(5)] string CheckoutSessionId = ""
) : Synapse;

    // Carries the raw Stripe event the webhook received; the marketplace verifies it via the
    // Stripe connector and only then records the purchase + issues the license.
    [GenerateSerializer]
    public sealed record ConfirmCheckoutCommand([property: Id(1)] string StripeEventJson,
    [property: Id(2)] string StripeSignature
) : Synapse;

    [GenerateSerializer]
    public sealed record ConfirmCheckoutResponse([property: Id(1)] bool Success,
    [property: Id(2)] string LicenseToken,
    [property: Id(3)] string Message
) : Synapse;

    [GenerateSerializer]
    public sealed record PublishBundleCommand([property: Id(1)] string BundleId,
    [property: Id(2)] string Version,
    [property: Id(3)] string ManifestJson,
    [property: Id(4)] byte[] ZipBytes
) : Synapse;

    [GenerateSerializer]
    public sealed record PublishBundleResponse([property: Id(1)] bool Success,
    [property: Id(2)] List<string> Diagnostics
) : Synapse;

    [GenerateSerializer]
    public sealed record InstallMarketplaceNeuronCommand([property: Id(1)] string BundleId,
    [property: Id(2)] string UserId
) : Synapse;

    [GenerateSerializer]
    public sealed record InstallMarketplaceNeuronResponse([property: Id(1)] bool Success,
    [property: Id(2)] List<string> Diagnostics
) : Synapse;

    // ==========================================
    // LICENSING SYNAPSES
    // ==========================================

    [GenerateSerializer]
    public sealed record VerifyLicenseQuery([property: Id(1)] string LicenseToken,
    [property: Id(2)] string BundleId,
    [property: Id(3)] string UserId
) : Synapse;

    [GenerateSerializer]
    public sealed record VerifyLicenseResponse([property: Id(1)] bool IsValid,
    [property: Id(2)] string Reason
) : Synapse;

    [GenerateSerializer]
    public sealed record IssueLicenseCommand([property: Id(1)] string BundleId,
    [property: Id(2)] string UserId
) : Synapse;

    [GenerateSerializer]
    public sealed record IssueLicenseResponse([property: Id(1)] string LicenseToken
) : Synapse;

    // ==========================================
    // DATABASE SYNAPSES (PostgresDbNeuron interaction)
    // ==========================================

    [GenerateSerializer]
    public sealed record DbInsertBundle([property: Id(1)] BundleInfo Bundle
) : Synapse;

    [GenerateSerializer]
    public sealed record DbInsertBundleReply([property: Id(1)] bool Success
) : Synapse;

    [GenerateSerializer]
    public sealed record DbSelectBundles : Synapse;

    [GenerateSerializer]
    public sealed record DbSelectBundlesReply([property: Id(1)] List<BundleInfo> Bundles
) : Synapse;

    [GenerateSerializer]
    public sealed record DbInsertPurchase([property: Id(1)] PurchaseRow Purchase
) : Synapse;

    [GenerateSerializer]
    public sealed record DbInsertPurchaseReply([property: Id(1)] bool Success
) : Synapse;

    [GenerateSerializer]
    public sealed record DbInsertLicense([property: Id(1)] LicenseRow License
) : Synapse;

    [GenerateSerializer]
    public sealed record DbInsertLicenseReply([property: Id(1)] bool Success
) : Synapse;

    [GenerateSerializer]
    public sealed record DbSelectLicenses([property: Id(1)] string UserId,
    [property: Id(2)] string BundleId
) : Synapse;

    [GenerateSerializer]
    public sealed record DbSelectLicensesReply([property: Id(1)] List<LicenseRow> Licenses
) : Synapse;
}
