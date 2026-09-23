using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using Orleans.Metadata;

namespace DigitalBrain.Marketplace.Creators;

// Publishing opens in rings: first-party, then invited remote apps, then creators' declarative apps,
// and last sandboxed process apps. P5a only serves the declarative ring.
public enum PublishingRing
{
    FirstParty = 0,
    InvitedRemote = 1,
    CreatorDeclarative = 2,
    SandboxedProcess = 3,
}

public enum CreatorListingState
{
    Published = 0,
    Killed = 1,
}

public enum CreatorPublishOutcome
{
    Published = 0,
    InvalidManifest = 1,
    RejectedNotDeclarative = 2,
    IdentityDenied = 3,
    CallFilterDenied = 4,
    KillSwitchActive = 5,
    NotCertified = 6,
    ReConsentRequired = 7,
}

[GenerateSerializer, Alias("marketplace.scenario-evidence")]
public sealed record ScenarioEvidence
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required bool Passed { get; init; }
    [Id(2)] public required string Detail { get; init; }
}

// Deterministic-fake certification: every declared scenario ran on a fake and left evidence. A
// scenario that depends on a live model can never be a gate (D11).
[GenerateSerializer, Alias("marketplace.creator-certification")]
public sealed record CertificationReport
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Version { get; init; }
    [Id(2)] public required bool Certified { get; init; }
    [Id(3)] public required string CertifiedBy { get; init; }
    [Id(4)] public required DateTimeOffset CertifiedAt { get; init; }
    [Id(5)] public IReadOnlyList<ScenarioEvidence> Scenarios { get; init; } = [];
}

[GenerateSerializer, Alias("marketplace.price-increase")]
public sealed record PriceIncrease
{
    [Id(0)] public required string MeterId { get; init; }
    [Id(1)] public required decimal From { get; init; }
    [Id(2)] public required decimal To { get; init; }
}

// A new version may only widen permission or price after the publisher re-consents. The fingerprint
// is stable for the exact change so consent cannot be replayed onto a different one.
[GenerateSerializer, Alias("marketplace.consent-change")]
public sealed record ConsentChange
{
    [Id(0)] public IReadOnlyList<string> AddedPermissions { get; init; } = [];
    [Id(1)] public IReadOnlyList<PriceIncrease> PriceIncreases { get; init; } = [];
    [Id(2)] public required string Fingerprint { get; init; }
}

[GenerateSerializer, Alias("marketplace.creator-listing")]
public sealed record CreatorListing
{
    [Id(0)] public required string ListingId { get; init; }
    [Id(1)] public required string AppId { get; init; }
    [Id(2)] public required string Version { get; init; }
    [Id(3)] public required string CreatorPrincipal { get; init; }
    [Id(4)] public required PublishingRing Ring { get; init; }
    [Id(5)] public required CreatorListingState State { get; init; }
    [Id(6)] public required AppManifest Manifest { get; init; }
    [Id(7)] public required CertificationReport Certification { get; init; }
    [Id(8)] public required DateTimeOffset PublishedAt { get; init; }
    [Id(9)] public bool Beta { get; init; } = true;
}

// A saved declarative app publishes in one step. The request carries the manifest and the caller;
// the app needs no sandbox because it carries no code.
[GenerateSerializer, Alias("marketplace.publish-declarative")]
public sealed record PublishDeclarativeRequest
{
    [Id(0)] public required AppManifest Manifest { get; init; }
    [Id(1)] public required CallerContext Caller { get; init; }
    [Id(2)] public string? ReConsentFingerprint { get; init; }
}

[GenerateSerializer, Alias("marketplace.publish-result")]
public sealed record PublishResult
{
    [Id(0)] public required CreatorPublishOutcome Outcome { get; init; }
    [Id(1)] public CreatorListing? Listing { get; init; }
    [Id(2)] public CertificationReport? Certification { get; init; }
    [Id(3)] public ConsentChange? ReConsent { get; init; }
    [Id(4)] public string? Explanation { get; init; }
}

// D7/D16 stay behind flags that default off until the owner closes G-1/G-3.
[GenerateSerializer, Alias("marketplace.creator-features")]
public sealed record CreatorPublishingFeatures
{
    [Id(0)] public bool SelfServeSignUp { get; init; }
    [Id(1)] public bool PrepaidCompute { get; init; }
    [Id(2)] public bool ConsumerTerms { get; init; }
}

[GenerateSerializer, Alias("marketplace.kill-status")]
public sealed record KillSwitchStatus
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Reason { get; init; }
    [Id(2)] public required DateTimeOffset KilledAt { get; init; }
}

public interface IMarketplaceKillSwitch
{
    bool IsKilled(string appId);

    KillSwitchStatus? Status(string appId);

    KillSwitchStatus Kill(string appId, string reason);

    void Restore(string appId);
}

// Runs the manifest's scenarios on deterministic fakes and returns evidence. No live model, no
// network: a scenario that cannot pass deterministically fails certification.
public interface ICreatorScenarioCertifier
{
    Task<CertificationReport> CertifyAsync(AppManifest manifest, CancellationToken cancellationToken = default);
}

[Alias("marketplace.creator-publishing"), DefaultGrainType("marketplace.creator-publishing")]
public interface ICreatorPublishing : INeuron
{
    Task<PublishResult> Publish(PublishDeclarativeRequest request);

    Task<CreatorPublishingFeatures> Features();

    Task<CreatorListing?> Read(string appId);

    Task<IReadOnlyList<CreatorListing>> List();

    Task<CreatorListing> Kill(string appId, string reason);

    Task<CreatorListing> Restore(string appId);
}
