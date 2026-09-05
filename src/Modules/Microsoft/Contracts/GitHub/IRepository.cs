using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github-repository")]
public interface IRepository : IAgent, IHandle<ReadPullRequest>, IHandle<ReadPullRequests>;

[GenerateSerializer, Alias("github.read-pull-request")]
public sealed record ReadPullRequest([property: Id(0)] int Number) : Signal<PullRequestRead>;

[GenerateSerializer, Alias("github.pull-request-read")]
public sealed record PullRequestRead(
    [property: Id(0)] PullRequestSnapshot? Snapshot,
    [property: Id(1)] bool Available) : Signal;

[GenerateSerializer, Alias("github.read-pull-requests")]
public sealed record ReadPullRequests : Signal<PullRequestsRead>;

[GenerateSerializer, Alias("github.pull-requests-read")]
public sealed record PullRequestsRead(
    [property: Id(0)] PullRequestSnapshot[] Snapshots,
    [property: Id(1)] bool Available) : Signal;

[GenerateSerializer, Alias("github.pull-request-opened")]
public sealed record PullRequestOpened(
    [property: Id(0)] string BindingId,
    [property: Id(1)] NeuronId Repository,
    [property: Id(2)] PullRequestSnapshot Snapshot,
    [property: Id(3)] string EventId) : Signal;

[GenerateSerializer, Alias("github.pull-request-updated")]
public sealed record PullRequestUpdated(
    [property: Id(0)] string BindingId,
    [property: Id(1)] NeuronId Repository,
    [property: Id(2)] PullRequestSnapshot Snapshot,
    [property: Id(3)] string EventId) : Signal;

[GenerateSerializer, Alias("github.pull-request-closed")]
public sealed record PullRequestClosed(
    [property: Id(0)] string BindingId,
    [property: Id(1)] NeuronId Repository,
    [property: Id(2)] PullRequestSnapshot Snapshot,
    [property: Id(3)] string EventId) : Signal;

[GenerateSerializer, Alias("github.pull-request-checks-changed")]
public sealed record PullRequestChecksChanged(
    [property: Id(0)] string BindingId,
    [property: Id(1)] NeuronId Repository,
    [property: Id(2)] PullRequestSnapshot Snapshot,
    [property: Id(3)] string EventId) : Signal;

[GenerateSerializer, Alias("github.repository-access-revoked")]
public sealed record RepositoryAccessRevoked(
    [property: Id(0)] string BindingId,
    [property: Id(1)] NeuronId Repository,
    [property: Id(2)] string EventId) : Signal;
