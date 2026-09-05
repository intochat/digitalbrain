using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("db.github.pull-request-review")]
public interface IPullRequestReview : INeuron,
    IHandle<PullRequestOpened>, IHandle<PullRequestUpdated>, IHandle<PullRequestClosed>, IHandle<PullRequestChecksChanged>, IHandle<RepositoryAccessRevoked>,
    IHandle<EnablePullRequestReview>, IHandle<DisablePullRequestReview>, IHandle<ReadReviewCandidates>,
    IHandle<StartPullRequestReview>, IHandle<CancelPullRequestReview>, IHandle<ReadReviewResults>, IHandle<PublishPullRequestReview>;

[Alias("db.github.architecture-reviewer")]
public interface IArchitectureReviewer : IAgent;

[Alias("db.github.code-quality-reviewer")]
public interface ICodeQualityReviewer : IAgent;

[GenerateSerializer, Alias("db.github.enable-review")]
public sealed record EnablePullRequestReview(
    [property: Id(0)] string BindingId, [property: Id(1)] string BehaviorName,
    [property: Id(2)] Guid BehaviorRevision, [property: Id(3)] DateTimeOffset ObserveAfter) : Signal<ReviewConfiguration>;

[GenerateSerializer, Alias("db.github.disable-review")]
public sealed record DisablePullRequestReview : Signal<ReviewConfiguration>;

[GenerateSerializer, Alias("db.github.review-configuration")]
public sealed record ReviewConfiguration([property: Id(0)] bool Enabled, [property: Id(1)] string? BindingId,
    [property: Id(2)] string? BehaviorName, [property: Id(3)] Guid BehaviorRevision) : Signal;

[GenerateSerializer, Alias("db.github.read-review-candidates")]
public sealed record ReadReviewCandidates : Signal<ReviewCandidates>;

[GenerateSerializer, Alias("db.github.review-candidates")]
public sealed record ReviewCandidates([property: Id(0)] bool Enabled,
    [property: Id(1)] PullRequestSnapshot[] Candidates) : Signal;

[GenerateSerializer, Alias("db.github.check-requirement")]
public sealed record GitHubCheckRequirement([property: Id(0)] string Name,
    [property: Id(1)] long? AppId = null, [property: Id(2)] string Kind = "check");

[GenerateSerializer, Alias("db.github.start-review")]
public sealed record StartPullRequestReview(
    [property: Id(0)] PullRequestSnapshot Expected,
    [property: Id(1)] Guid BehaviorRevision,
    [property: Id(2)] GitHubCheckRequirement[] RequiredChecks,
    [property: Id(3)] string[] AcceptedConclusions,
    [property: Id(4)] AgentRequest Architecture,
    [property: Id(5)] AgentRequest CodeQuality,
    [property: Id(6)] NeuronId Destination,
    [property: Id(7)] int MaxAttempts = 2) : Signal<ReviewAdmission>;

[GenerateSerializer, Alias("db.github.review-admission")]
public sealed record ReviewAdmission([property: Id(0)] Guid RunId, [property: Id(1)] string Status,
    [property: Id(2)] string? Detail = null) : Signal;

[GenerateSerializer, Alias("db.github.cancel-review")]
public sealed record CancelPullRequestReview([property: Id(0)] Guid RunId) : Signal<ReviewAdmission>;

[GenerateSerializer, Alias("db.github.read-review-results")]
public sealed record ReadReviewResults : Signal<ReviewResults>;

[GenerateSerializer, Alias("db.github.review-results")]
public sealed record ReviewResults([property: Id(0)] ReviewResult[] Results) : Signal;

[GenerateSerializer, Alias("db.github.publish-review")]
public sealed record PublishPullRequestReview([property: Id(0)] Guid RunId,
    [property: Id(1)] string Text) : Signal<ReviewAdmission>;

[GenerateSerializer, Alias("db.github.review-role-result")]
public sealed record ReviewRoleResult([property: Id(0)] string Role, [property: Id(1)] string Status,
    [property: Id(2)] string? Text, [property: Id(3)] int Attempt, [property: Id(4)] string? Detail = null);

[GenerateSerializer, Alias("db.github.review-result")]
public sealed record ReviewResult([property: Id(0)] Guid RunId, [property: Id(1)] PullRequestSnapshot Snapshot,
    [property: Id(2)] Guid BehaviorRevision, [property: Id(3)] string Status, [property: Id(4)] string? EvidenceHash,
    [property: Id(5)] ReviewRoleResult? Architecture, [property: Id(6)] ReviewRoleResult? CodeQuality,
    [property: Id(7)] bool Published, [property: Id(8)] string? Detail = null);

[GenerateSerializer, Alias("db.github.review-lifecycle")]
public sealed record PullRequestReviewChanged([property: Id(0)] Guid RunId,
    [property: Id(1)] int Number, [property: Id(2)] string Status, [property: Id(3)] string? Role = null) : Signal;
