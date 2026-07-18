using DigitalBrain.Core.Synapses;
using DigitalBrain.Abstractions.Bundles;
using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Distribution;

[GenerateSerializer]
public readonly record struct AccountId
{
    public AccountId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Account id must be a non-empty value.", nameof(value));
        }

        Value = value.Trim();
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}

[GenerateSerializer]
public enum AuthProvider
{
    Google = 0
}

[GenerateSerializer]
public readonly record struct AuthSubject([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer]
public sealed record AuthIdentity(
    [property: Id(0)] AuthProvider Provider,
    [property: Id(1)] AuthSubject Subject);

[GenerateSerializer]
public sealed record SellerMetadata(
    [property: Id(0)] string PayoutAccountRef);

[GenerateSerializer]
public sealed record Account(
    [property: Id(0)] AccountId AccountId,
    [property: Id(1)] AuthIdentity AuthIdentity,
    [property: Id(2)] SellerMetadata Seller);

[GenerateSerializer]
public readonly record struct LicenseToken
{
    public const string SourceIncludedToken = "source-included";

    public static LicenseToken SourceIncluded { get; } = new(SourceIncludedToken);

    [JsonConstructor]
    public LicenseToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("License token must be a non-empty value.", nameof(value));
        }

        Value = value.Trim();
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}

[GenerateSerializer]
public readonly record struct BundleVersionPolicy
{
    public BundleVersionPolicy(BundleVersion? version, bool isLatest)
    {
        Version = version;
        IsLatest = isLatest;
    }

    [Id(0)]
    public BundleVersion? Version { get; }

    [Id(1)]
    public bool IsLatest { get; }

    public static BundleVersionPolicy Latest { get; } = new(null, true);

    public static BundleVersionPolicy Exact(BundleVersion version) => new(version, false);

    public bool Allows(BundleVersion version) =>
        IsLatest || Version == version;
}

[GenerateSerializer]
public sealed record Entitlement(
    [property: Id(0)] AccountId AccountId,
    [property: Id(1)] BundleId BundleId,
    [property: Id(2)] BundleVersionPolicy VersionPolicy,
    [property: Id(3)] LicenseToken LicenseToken,
    [property: Id(4)] DateTimeOffset GrantedAtUtc);

[GenerateSerializer]
public enum DownloadDenialReason
{
    None = 0,
    NoEntitlement = 1,
    BundleNotFound = 2
}

public static class DownloadDenialReasonTokens
{
    public const string NoEntitlement = "no entitlement";
    public const string BundleNotFound = "bundle not found";

    public static string ToToken(this DownloadDenialReason reason) => reason switch
    {
        DownloadDenialReason.NoEntitlement => NoEntitlement,
        DownloadDenialReason.BundleNotFound => BundleNotFound,
        _ => string.Empty
    };
}

[GenerateSerializer]
public sealed record AccountRegistrationRequest(
    [property: Id(0)] AccountId AccountId,
    [property: Id(1)] AuthIdentity AuthIdentity,
    [property: Id(2)] SellerMetadata Seller);

[GenerateSerializer]
public sealed record AccountRegistrationResult(
    [property: Id(0)] Account Account,
    [property: Id(1)] AccountRegistered Audit);

[GenerateSerializer]
public sealed record EntitlementGrantResult(
    [property: Id(0)] Entitlement Entitlement,
    [property: Id(1)] EntitlementGranted Audit);

[GenerateSerializer]
public sealed record EntitledBundleDownloadResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] BundleDownload? Bundle,
    [property: Id(2)] Entitlement? Entitlement,
    [property: Id(3)] EntitlementGranted? EntitlementAudit,
    [property: Id(4)] DownloadDenialReason DenialReason,
    [property: Id(5)] IReadOnlyList<string> Diagnostics);

[GenerateSerializer]
public sealed record PortableLicenseValidationResult(
    [property: Id(0)] bool IsValid,
    [property: Id(1)] Entitlement? Entitlement,
    [property: Id(2)] string Diagnostic);

[GenerateSerializer]
public sealed record PortableLicenseActivationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Diagnostic);

public interface IAccountRegistry
{
    Task<AccountRegistrationResult> RegisterAsync(
        AccountRegistrationRequest request,
        CancellationToken cancellationToken = default);

    Task<Account?> FindByIdAsync(
        AccountId accountId,
        CancellationToken cancellationToken = default);

    Task<Account?> FindByAuthIdentityAsync(
        AuthIdentity authIdentity,
        CancellationToken cancellationToken = default);
}

public interface IEntitlementStore
{
    Task SaveAsync(
        Entitlement entitlement,
        CancellationToken cancellationToken = default);

    Task<Entitlement?> FindAsync(
        AccountId accountId,
        BundleId bundleId,
        BundleVersion version,
        CancellationToken cancellationToken = default);
}

public interface IPortableLicenseService
{
    Task<LicenseToken> IssueAsync(
        AccountId accountId,
        BundleId bundleId,
        BundleVersionPolicy versionPolicy,
        CancellationToken cancellationToken = default);

    PortableLicenseValidationResult ValidateOffline(
        LicenseToken token,
        AccountId accountId,
        BundleId bundleId,
        BundleVersion version);
}

public interface IEntitlementPolicy
{
    Task<EntitledBundleDownloadResult> AuthorizeDownloadAsync(
        AccountId accountId,
        PublishedBundleManifest manifest,
        CancellationToken cancellationToken = default);
}

public interface IEntitledBundleDownloadService
{
    Task<EntitledBundleDownloadResult> DownloadAsync(
        AccountId accountId,
        BundleId bundleId,
        BundleVersionSelector selector,
        CancellationToken cancellationToken = default);
}

public interface IPortableBundleActivator
{
    Task<PortableLicenseActivationResult> ActivateAsync(
        LicenseToken token,
        AccountId accountId,
        BundleId bundleId,
        BundleVersion version,
        CancellationToken cancellationToken = default);
}

[GenerateSerializer]
public sealed record AccountRegistered(
    [property: Id(0)] AccountId AccountId,
    [property: Id(1)] AuthIdentity AuthIdentity) : Synapse;

[GenerateSerializer]
public sealed record EntitlementGranted(
    [property: Id(0)] AccountId AccountId,
    [property: Id(1)] BundleId BundleId,
    [property: Id(2)] BundleVersionPolicy VersionPolicy,
    [property: Id(3)] LicenseToken LicenseToken) : Synapse;
