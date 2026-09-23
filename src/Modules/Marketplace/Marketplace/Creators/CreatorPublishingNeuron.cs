using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Orleans.Runtime;

namespace DigitalBrain.Marketplace.Creators;

[GenerateSerializer, Alias("marketplace.creator-publishing-state")]
internal sealed record CreatorPublishingState
{
    [Id(0)] public Dictionary<string, List<CreatorListing>> Listings { get; init; } = [];
}

// One marketplace publisher for the whole silo. A saved declarative app publishes in one step:
// identity, the call filter, certification on deterministic fakes, the kill switch and re-consent on
// a grown version. Non-declarative kinds are rejected because code never runs in the silo.
[GrainType("marketplace.creator-publishing")]
internal sealed class CreatorPublishingNeuron(
    [PersistentState("marketplace.creator-publishing", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CreatorPublishingState> store,
    ICallFilter filter,
    ICreatorScenarioCertifier certifier,
    IMarketplaceKillSwitch killSwitch,
    CreatorPublishingOptions options)
    : Neuron<CreatorPublishingState>(store), ICreatorPublishing
{
    public async Task<PublishResult> Publish(PublishDeclarativeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = request.Caller;
        if (!CallerContextStamper.IsTrusted(caller) || caller.Kind != CallerKind.User || caller.StampedBy != TrustedEdge.AuthenticatedHttp)
        {
            return Reject(CreatorPublishOutcome.IdentityDenied, "Publishing needs an authenticated user principal.");
        }

        if (request.Manifest.Kind != AppKind.Declarative)
        {
            return Reject(CreatorPublishOutcome.RejectedNotDeclarative, "Only declarative apps publish without a sandbox; code apps are P5b.");
        }

        try
        {
            ManifestValidator.Validate(request.Manifest);
        }
        catch (AppManifestException invalid)
        {
            return Reject(CreatorPublishOutcome.InvalidManifest, invalid.Message);
        }

        var versions = Versions(request.Manifest.Id);
        var latest = versions.Count > 0 ? versions[^1] : null;
        if (latest is not null && !string.Equals(latest.CreatorPrincipal, caller.PrincipalId, StringComparison.Ordinal))
        {
            return Reject(CreatorPublishOutcome.IdentityDenied, "Only the app's creator may publish a new version.");
        }

        var decision = await filter.AuthorizeAsync(new CallRequest
        {
            Caller = caller,
            TargetNeuron = "marketplace.creator-publishing",
            Operation = "Publish",
            SemanticTypeIds = request.Manifest.Permissions.Select(permission => permission.SemanticTypeId).ToArray(),
            HasSideEffects = true,
        });
        if (!decision.Allowed)
        {
            return Reject(CreatorPublishOutcome.CallFilterDenied, decision.Explanation ?? "Denied by the call filter.");
        }

        if (killSwitch.IsKilled(request.Manifest.Id))
        {
            return Reject(CreatorPublishOutcome.KillSwitchActive, "The app is switched off and cannot publish.");
        }

        if (latest is not null && string.Equals(latest.Version, request.Manifest.Version, StringComparison.Ordinal))
        {
            return new PublishResult { Outcome = CreatorPublishOutcome.Published, Listing = latest, Certification = latest.Certification };
        }

        var report = await certifier.CertifyAsync(request.Manifest);
        if (!report.Certified)
        {
            return new PublishResult
            {
                Outcome = CreatorPublishOutcome.NotCertified,
                Certification = report,
                Explanation = "The scenarios did not pass on the deterministic fakes.",
            };
        }

        var growth = latest is null ? null : PublishingRules.DetectGrowth(latest.Manifest, request.Manifest);
        if (growth is not null && !PublishingRules.Accepts(growth, request.ReConsentFingerprint))
        {
            await PublishAsync(new CreatorReConsentRequired(request.Manifest.Id, request.Manifest.Version, growth.AddedPermissions));
            return new PublishResult
            {
                Outcome = CreatorPublishOutcome.ReConsentRequired,
                ReConsent = growth,
                Certification = report,
                Explanation = "The new version widens permissions or prices; the publisher must re-consent.",
            };
        }

        var listing = new CreatorListing
        {
            ListingId = $"{request.Manifest.Id}@{request.Manifest.Version}",
            AppId = request.Manifest.Id,
            Version = request.Manifest.Version,
            CreatorPrincipal = caller.PrincipalId,
            Ring = PublishingRing.CreatorDeclarative,
            State = CreatorListingState.Published,
            Manifest = request.Manifest,
            Certification = report,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        var next = new List<CreatorListing>(versions) { listing };
        var state = Store(request.Manifest.Id, next);
        await Save(state, new AppPublished(listing.AppId, listing.Version, listing.Ring.ToString(), listing.PublishedAt));
        return new PublishResult { Outcome = CreatorPublishOutcome.Published, Listing = listing, Certification = report };
    }

    public Task<CreatorPublishingFeatures> Features() => Task.FromResult(new CreatorPublishingFeatures
    {
        SelfServeSignUp = options.SelfServeSignUp,
        PrepaidCompute = options.PrepaidCompute,
        ConsumerTerms = options.ConsumerTerms,
    });

    public Task<CreatorListing?> Read(string appId)
    {
        var versions = Versions(appId);
        return Task.FromResult(versions.Count > 0 ? versions[^1] : null);
    }

    public Task<IReadOnlyList<CreatorListing>> List()
    {
        IReadOnlyList<CreatorListing> active = [.. Snapshot.Listings.Values
            .Where(versions => versions.Count > 0)
            .Select(versions => versions[^1])];
        return Task.FromResult(active);
    }

    public async Task<CreatorListing> Kill(string appId, string reason)
    {
        var status = killSwitch.Kill(appId, reason);
        var versions = Versions(appId);
        if (versions.Count == 0) { throw new KeyNotFoundException($"App '{appId}' has no published version."); }
        var killed = versions.Select(version => version with { State = CreatorListingState.Killed }).ToList();
        await Save(Store(appId, killed), new CreatorAppKilled(appId, status.Reason));
        return killed[^1];
    }

    public async Task<CreatorListing> Restore(string appId)
    {
        killSwitch.Restore(appId);
        var versions = Versions(appId);
        if (versions.Count == 0) { throw new KeyNotFoundException($"App '{appId}' has no published version."); }
        var restored = versions.Select(version => version with { State = CreatorListingState.Published }).ToList();
        await Save(Store(appId, restored), new AppPublished(appId, versions[^1].Version, PublishingRing.CreatorDeclarative.ToString(), DateTimeOffset.UtcNow));
        return restored[^1];
    }

    private List<CreatorListing> Versions(string appId) =>
        Snapshot.Listings.TryGetValue(appId, out var versions) ? [.. versions] : [];

    private CreatorPublishingState Store(string appId, List<CreatorListing> versions)
    {
        var next = new Dictionary<string, List<CreatorListing>>(Snapshot.Listings, StringComparer.Ordinal)
        {
            [appId] = versions,
        };
        return new CreatorPublishingState { Listings = next };
    }

    private static PublishResult Reject(CreatorPublishOutcome outcome, string explanation) =>
        new() { Outcome = outcome, Explanation = explanation };
}
