using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Inbox;
using DigitalBrain.Marketplace.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Marketplace;

[GenerateSerializer, Alias("marketplace.catalog-state")]
internal sealed record ListingCatalogState
{
    [Id(0)] public Dictionary<string, AppListing> Listings { get; init; } = [];
    [Id(1)] public Dictionary<string, List<AppReview>> Reviews { get; init; } = [];
    [Id(2)] public Dictionary<string, List<AppReport>> Reports { get; init; } = [];
    [Id(3)] public DateTimeOffset? RankingPublishedAt { get; init; }
}

// The catalog service. It is separate from any package feed: listings are generated from manifests,
// every listing carries its certification evidence, and governance actions (kill switch, takedown,
// reports) are durable and signalled.
[GrainType("listing-catalog")]
internal sealed class ListingCatalogNeuron(
    [PersistentState("marketplace", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ListingCatalogState> store,
    IAppUsageLedger usage) : Neuron<ListingCatalogState>(store), IListingCatalog
{
    private const string PlatformInbox = "platform";

    public async Task<AppListing> PublishAsync(AppManifest manifest, PublisherProfile publisher, CertificationEvidence certification)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(certification);
        if (!certification.Passed)
        {
            throw new InvalidOperationException(
                $"App '{manifest.Id}' is not certified ({certification.Outcome}); it cannot be listed.");
        }
        if (!string.Equals(certification.AppId, manifest.Id, StringComparison.Ordinal)
            || !string.Equals(certification.Version, manifest.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Certification evidence does not match the manifest.");
        }

        var status = publisher.Ring == PublishRing.FirstParty ? ListingStatus.Published : ListingStatus.Beta;
        var listing = new AppListing
        {
            Manifest = manifest,
            PublisherId = publisher.PublisherId,
            Ring = publisher.Ring,
            Status = status,
            Certification = certification,
            ListedAt = DateTimeOffset.UtcNow,
        };
        var next = Snapshot with { Listings = Replace(Snapshot.Listings, manifest.Id, listing) };
        await Save(next, new AppListed(manifest.Id, manifest.Version, status, publisher.Ring, listing.ListedAt));
        return listing;
    }

    public Task<CertificationEvidence> CertifyAsync(AppManifest manifest, PublisherProfile publisher, AppSignature? signature, byte[]? artifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(publisher);
        var certification = ServiceProvider.GetRequiredService<ICertificationService>();
        return certification.CertifyAsync(new CertificationRequest
        {
            Manifest = manifest,
            Publisher = publisher,
            Signature = signature,
            Artifact = artifact,
        }, cancellationToken);
    }

    public Task RecordUsageAsync(string appId, string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        _ = Require(appId);
        usage.RecordRun(appId, workspaceId);
        return Task.CompletedTask;
    }

    public Task<AppListing?> ReadAsync(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return Task.FromResult(Snapshot.Listings.TryGetValue(appId, out var listing) ? listing : null);
    }

    public Task<IReadOnlyList<AppListing>> ListAsync()
    {
        IReadOnlyList<AppListing> listings = [.. Snapshot.Listings.Values
            .OrderBy(listing => listing.Manifest.Id, StringComparer.Ordinal)];
        return Task.FromResult(listings);
    }

    public Task<IReadOnlyList<AppListing>> RankAsync(string intent)
    {
        var tokens = Tokens(intent);
        IReadOnlyList<AppListing> ranked = [.. Snapshot.Listings.Values
            .Where(listing => listing.Status is ListingStatus.Published or ListingStatus.Beta)
            .Select(listing => (Listing: listing, Score: Score(listing, tokens)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Listing.Manifest.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Listing)];
        return Task.FromResult(ranked);
    }

    public async Task AcceptIntentAsync(string appId, string intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        var listing = Require(appId);
        var accepted = listing with { AcceptedIntents = listing.AcceptedIntents + 1 };
        await Save(Snapshot with { Listings = Replace(Snapshot.Listings, appId, accepted) },
            new AppIntentAccepted(appId, intent, DateTimeOffset.UtcNow));
    }

    public Task<RankingParameters> ReadRankingParametersAsync() => Task.FromResult(new RankingParameters
    {
        Formula = "score = 10 * matchedIntentTokens + acceptedIntents",
        Signals = ["example-prompts", "operations", "accepted-intents"],
        PublishedAt = Snapshot.RankingPublishedAt ?? DateTimeOffset.UnixEpoch,
    });

    public async Task<AppListing> DisableAsync(string appId, string statementOfReasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(statementOfReasons);
        var listing = Require(appId);
        var disabled = listing with { Status = ListingStatus.Disabled, DisabledReason = statementOfReasons };
        var at = DateTimeOffset.UtcNow;
        await Save(Snapshot with { Listings = Replace(Snapshot.Listings, appId, disabled) },
            new AppDisabled(appId, statementOfReasons, at));
        await NotifyDisabledAsync(appId, statementOfReasons).ConfigureAwait(true);
        return disabled;
    }

    public async Task<AppListing> TakedownAsync(string appId, string reasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasons);
        var listing = Require(appId);
        var takenDown = listing with { Status = ListingStatus.TakenDown, TakedownReason = reasons };
        await Save(Snapshot with { Listings = Replace(Snapshot.Listings, appId, takenDown) },
            new AppTakenDown(appId, reasons, DateTimeOffset.UtcNow));
        return takenDown;
    }

    public async Task<AppReview> ReviewAsync(string appId, string workspaceId, int stars, string? comment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        if (stars is < 1 or > 5) { throw new ArgumentOutOfRangeException(nameof(stars), stars, "Stars must be 1..5."); }
        _ = Require(appId);
        if (!usage.HasRun(appId, workspaceId))
        {
            throw new InvalidOperationException(
                $"Workspace '{workspaceId}' cannot review '{appId}' because it never ran the app.");
        }

        var review = new AppReview
        {
            AppId = appId,
            WorkspaceId = workspaceId,
            Stars = stars,
            Comment = comment,
            At = DateTimeOffset.UtcNow,
        };
        var reviews = Snapshot.Reviews.TryGetValue(appId, out var stored) ? [.. stored, review] : new List<AppReview> { review };
        await Save(Snapshot with { Reviews = Replace(Snapshot.Reviews, appId, reviews) },
            new AppReviewed(appId, workspaceId, stars, review.At));
        return review;
    }

    public Task<IReadOnlyList<AppReview>> ReadReviewsAsync(string appId)
    {
        IReadOnlyList<AppReview> reviews = Snapshot.Reviews.TryGetValue(appId, out var stored) ? [.. stored] : [];
        return Task.FromResult(reviews);
    }

    public async Task ReportAsync(string appId, string workspaceId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _ = Require(appId);
        var report = new AppReport
        {
            AppId = appId,
            WorkspaceId = workspaceId,
            Reason = reason,
            At = DateTimeOffset.UtcNow,
        };
        var reports = Snapshot.Reports.TryGetValue(appId, out var stored) ? [.. stored, report] : new List<AppReport> { report };
        await Save(Snapshot with { Reports = Replace(Snapshot.Reports, appId, reports) },
            new AppReported(appId, workspaceId, reason, report.At));
    }

    public Task<IReadOnlyList<AppReport>> ReadReportsAsync(string appId)
    {
        IReadOnlyList<AppReport> reports = Snapshot.Reports.TryGetValue(appId, out var stored) ? [.. stored] : [];
        return Task.FromResult(reports);
    }

    private AppListing Require(string appId) => Snapshot.Listings.TryGetValue(appId, out var listing)
        ? listing
        : throw new KeyNotFoundException($"App '{appId}' is not listed.");

    private Dictionary<string, TValue> Replace<TValue>(Dictionary<string, TValue> source, string key, TValue value)
        => new(source, StringComparer.Ordinal) { [key] = value };

    private async Task NotifyDisabledAsync(string appId, string statementOfReasons)
    {
#pragma warning disable CA1031 // the kill switch must not fail because the Inbox is unavailable
        try
        {
            await GrainFactory.GetGrain<IInboxFeed>(PlatformInbox).Post(new InboxItemDraft
            {
                Kind = InboxItemKind.AppDisabled,
                Title = $"App '{appId}' was disabled",
                GroupingKey = $"app-disabled:{appId}",
                Detail = statementOfReasons,
                AppId = appId,
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }

    private static int Score(AppListing listing, IReadOnlySet<string> intentTokens)
    {
        var vocabulary = Tokens(listing.Manifest.Name)
            .Concat(listing.Manifest.ExamplePrompts.SelectMany(Tokens))
            .Concat(listing.Manifest.Operations.SelectMany(operation => Tokens(operation.Name)))
            .ToHashSet(StringComparer.Ordinal);
        var matched = intentTokens.Count(vocabulary.Contains);
        return (10 * matched) + listing.AcceptedIntents;
    }

    private static HashSet<string> Tokens(string text) =>
        [.. text.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 4)
            .Select(token => token.ToLowerInvariant())];

    private static readonly char[] Separators =
        [' ', '\t', '\n', '\r', ',', '.', ':', ';', '!', '?', '(', ')', '[', ']', '"', '\'', '-', '_', '/', '\\'];
}
