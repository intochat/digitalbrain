using DigitalBrain.Apps;
using DigitalBrain.Contracts;

namespace DigitalBrain.Marketplace;

// A listing is generated from a manifest and carries its certification evidence. The catalog is a
// service separate from the package feed: the feed stores bits, the catalog answers what may run.
[GenerateSerializer, Alias("marketplace.listing")]
public sealed record AppListing
{
    [Id(0)] public required AppManifest Manifest { get; init; }
    [Id(1)] public required string PublisherId { get; init; }
    [Id(2)] public required PublishRing Ring { get; init; }
    [Id(3)] public required ListingStatus Status { get; init; }
    [Id(4)] public required CertificationEvidence Certification { get; init; }
    [Id(5)] public required DateTimeOffset ListedAt { get; init; }
    [Id(6)] public int AcceptedIntents { get; init; }
    [Id(7)] public string? DisabledReason { get; init; }
    [Id(8)] public string? TakedownReason { get; init; }

    public bool Beta => Status == ListingStatus.Beta;
}

// Ranking inputs are published so the ordering is auditable and never a black box.
[GenerateSerializer, Alias("marketplace.ranking-parameters")]
public sealed record RankingParameters
{
    [Id(0)] public required string Formula { get; init; }
    [Id(1)] public required IReadOnlyList<string> Signals { get; init; }
    [Id(2)] public required DateTimeOffset PublishedAt { get; init; }
}

[GenerateSerializer, Alias("marketplace.review")]
public sealed record AppReview
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string WorkspaceId { get; init; }
    [Id(2)] public required int Stars { get; init; }
    [Id(3)] public string? Comment { get; init; }
    [Id(4)] public required DateTimeOffset At { get; init; }
}

[GenerateSerializer, Alias("marketplace.report")]
public sealed record AppReport
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string WorkspaceId { get; init; }
    [Id(2)] public required string Reason { get; init; }
    [Id(3)] public required DateTimeOffset At { get; init; }
}

// The catalog service. It knows nothing about package storage; it owns listings, certification,
// governance (kill switch, takedown, reports), reviews from workspaces that ran the app, and the
// neutral, parameter-published ranking.
[Alias("listing-catalog"), Orleans.Metadata.DefaultGrainType("listing-catalog")]
public interface IListingCatalog : INeuron
{
    Task<AppListing> PublishAsync(AppManifest manifest, PublisherProfile publisher, CertificationEvidence certification);

    // Runs the full trust pipeline and returns the evidence a publisher then lists with.
    Task<CertificationEvidence> CertifyAsync(AppManifest manifest, PublisherProfile publisher, AppSignature? signature, byte[]? artifact, CancellationToken cancellationToken = default);

    // The app proxy reports that a workspace ran an app; only such workspaces may review it.
    Task RecordUsageAsync(string appId, string workspaceId);

    Task<AppListing?> ReadAsync(string appId);

    Task<IReadOnlyList<AppListing>> ListAsync();

    Task<IReadOnlyList<AppListing>> RankAsync(string intent);

    Task AcceptIntentAsync(string appId, string intent);

    Task<RankingParameters> ReadRankingParametersAsync();

    Task<AppListing> DisableAsync(string appId, string statementOfReasons);

    Task<AppListing> TakedownAsync(string appId, string reasons);

    Task<AppReview> ReviewAsync(string appId, string workspaceId, int stars, string? comment);

    Task<IReadOnlyList<AppReview>> ReadReviewsAsync(string appId);

    Task ReportAsync(string appId, string workspaceId, string reason);

    Task<IReadOnlyList<AppReport>> ReadReportsAsync(string appId);
}
