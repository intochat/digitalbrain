using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public sealed record PackExperience(
    ExperienceId ExperienceId,
    string? Description = null,
    string Version = "0.1.0") : Synapse;

[GenerateSerializer]
public sealed record ExperiencePacked(
    ExperienceManifest Manifest,
    string PackagePath) : Synapse;

[GenerateSerializer]
public sealed record PublishToMarketplace(
    ExperienceId ExperienceId,
    string? PackagePath = null,
    string? PeerAddress = null) : Synapse;

[GenerateSerializer]
public sealed record ExperienceListed(ExperienceListing Listing) : Synapse;

[GenerateSerializer]
public sealed record InstallFromMarketplace(
    ExperienceId ExperienceId,
    string? PeerAddress = null) : Synapse;

[GenerateSerializer]
public sealed record ExperienceDownloaded(
    ExperienceManifest Manifest,
    string LocalPackagePath,
    bool HashVerified) : Synapse;

[GenerateSerializer]
public sealed record RunExperience(ExperienceId Id) : Synapse;

[GenerateSerializer]
public sealed record UpdateBundle(
    ExperienceId ExperienceId,
    string? PeerAddress = null,
    string? TargetVersion = null) : Synapse;

[GenerateSerializer]
public sealed record StartQuarantineWorld(
    ExperienceId ExperienceId,
    string? PeerAddress = null) : Synapse;

[GenerateSerializer]
public sealed record QuarantinePromoted(
    ExperienceId ExperienceId,
    string WorldId,
    bool Green) : Synapse;

[GenerateSerializer]
public sealed record RuleReplayReport(
    [property: Id(0)] string ExperienceId,
    [property: Id(1)] RuleMatched[] Matched,
    [property: Id(2)] string[] ProducedEmitTypes,
    [property: Id(3)] RuleFault[] Faults) : Synapse;

[GenerateSerializer]
public sealed record GlobalPeer(
    [property: Id(0)] string Address,
    [property: Id(1)] DateTimeOffset LastSync,
    [property: Id(2)] bool Enabled = true);

[GenerateSerializer]
public sealed record SyncListingsToGlobal(
    [property: Id(0)] string ExperienceId) : Synapse;

[GenerateSerializer]
public sealed record GlobalListingsSynced(
    [property: Id(0)] string[] ExperienceIds,
    [property: Id(1)] DateTimeOffset At) : Synapse;

[GenerateSerializer]
public sealed record PullPopularFromGlobal() : Synapse;

[GenerateSerializer]
public sealed record GlobalListingsReceived(
    [property: Id(0)] string[] ExperienceIds) : Synapse;

[GenerateSerializer]
public sealed record RateExperience(
    [property: Id(0)] string ExperienceId,
    [property: Id(1)] int Rating,
    [property: Id(2)] string? Comment = null) : Synapse;

[GenerateSerializer]
public sealed record ExperienceRated(
    [property: Id(0)] string ExperienceId,
    [property: Id(1)] int Rating,
    [property: Id(2)] string? Comment,
    [property: Id(3)] DateTimeOffset At) : Synapse;

[GenerateSerializer]
public sealed record ExperienceRating(
    [property: Id(0)] string ExperienceId,
    [property: Id(1)] int Rating,
    [property: Id(2)] string? Comment,
    [property: Id(3)] DateTimeOffset At);

[GenerateSerializer]
public sealed record ListPublished() : Synapse;

[GenerateSerializer]
public sealed record RunDistributionSimulation() : Synapse;
