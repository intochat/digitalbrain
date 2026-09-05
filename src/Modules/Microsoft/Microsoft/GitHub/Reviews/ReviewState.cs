using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.AI;

namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer]
internal sealed record ReviewState
{
    [Id(0)] public bool Enabled { get; init; }
    [Id(1)] public string? BindingId { get; init; }
    [Id(2)] public string? BehaviorName { get; init; }
    [Id(3)] public Guid BehaviorRevision { get; init; }
    [Id(4)] public DateTimeOffset ObserveAfter { get; init; }
    [Id(5)] public ActorContext? Actor { get; init; }
    [Id(6)] public List<PullRequestSnapshot> Candidates { get; init; } = [];
    [Id(7)] public List<ReviewRun> Runs { get; init; } = [];
    [Id(8)] public bool RemoveSubscriptions { get; init; }
}

[GenerateSerializer]
internal sealed record ReviewRun
{
    [Id(0)] public Guid Id { get; init; }
    [Id(1)] public PullRequestSnapshot Snapshot { get; init; } = null!;
    [Id(2)] public Guid BehaviorRevision { get; init; }
    [Id(3)] public string BindingRevision { get; init; } = "";
    [Id(4)] public string Status { get; init; } = "pending";
    [Id(5)] public int Generation { get; init; }
    [Id(6)] public int Attempts { get; init; }
    [Id(7)] public int MaxAttempts { get; init; }
    [Id(8)] public GitHubCheckRequirement[] RequiredChecks { get; init; } = [];
    [Id(9)] public string[] AcceptedConclusions { get; init; } = [];
    [Id(10)] public AgentRequest ArchitectureRequest { get; init; } = null!;
    [Id(11)] public AgentRequest CodeQualityRequest { get; init; } = null!;
    [Id(12)] public NeuronId Destination { get; init; }
    [Id(13)] public GitHubReviewEvidence? Evidence { get; init; }
    [Id(14)] public ReviewRoleResult? Architecture { get; init; }
    [Id(15)] public ReviewRoleResult? CodeQuality { get; init; }
    [Id(16)] public string? Detail { get; init; }
    [Id(17)] public string? PublicationText { get; init; }
    [Id(18)] public bool Published { get; init; }
    [Id(19)] public DateTimeOffset StartedAt { get; init; }

    internal ReviewResult Result() => new(Id, Snapshot, BehaviorRevision, Status, Evidence?.Hash,
        Architecture, CodeQuality, Published, Detail);
}

[GenerateSerializer]
internal sealed record ReviewWork([property: Id(0)] NeuronId Inbox,
    [property: Id(1)] string BindingId, [property: Id(2)] ActorContext Actor,
    [property: Id(3)] ReviewRun Run);

[Alias("github-review-worker")]
internal interface IPullRequestReviewWorker : IGrainWithStringKey
{
    [Alias(nameof(RunAsync)), ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task RunAsync(ReviewWork work, CancellationToken cancellationToken);

    [Alias(nameof(SynchronizeAsync))]
    Task<PullRequestSnapshot[]> SynchronizeAsync(NeuronId inbox, string bindingId, ActorContext actor,
        bool enabled, CancellationToken cancellationToken);
}

[Alias("github-review-ledger")]
internal interface IReviewLedger : IGrainWithStringKey
{
    [Alias(nameof(StoreEvidenceAsync))]
    Task<bool> StoreEvidenceAsync(Guid runId, int generation, GitHubReviewEvidence evidence, PullRequestSnapshot verifiedSnapshot);

    [Alias(nameof(StoreRoleAsync))]
    Task StoreRoleAsync(Guid runId, int generation, ReviewRoleResult result, PullRequestSnapshot verifiedSnapshot);
}
