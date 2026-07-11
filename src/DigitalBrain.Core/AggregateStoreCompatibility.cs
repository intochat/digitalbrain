using System.Text.Json;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Core.V2;

// These four Orleans-serialized CLR identities predate stable aliases. Their names and
// namespace are persistence contracts until an explicit storage migration is deployed.
#pragma warning disable ORLEANS0010
[GenerateSerializer]
public sealed record V2InboxRecord(
    [property: Id(0)] string CommandId,
    [property: Id(1)] string? CommitId,
    [property: Id(2)] DateTimeOffset AcceptedAt);

[GenerateSerializer]
public sealed record V2AggregateSnapshot(
    [property: Id(0)] long CommitSequence,
    [property: Id(1)] JsonElement State,
    [property: Id(2)] IReadOnlyList<AggregateCommit> Commits,
    [property: Id(3)] IReadOnlyList<OutboxRecord> Outbox,
    [property: Id(4)] IReadOnlyList<EffectTransitionRecord> EffectTransitions,
    [property: Id(5)] IReadOnlyList<V2InboxRecord> Inbox);

[GenerateSerializer]
public sealed record V2CommitRequest(
    [property: Id(0)] string CommandId,
    [property: Id(1)] long ExpectedCommitSequence,
    [property: Id(2)] JsonElement NewState,
    [property: Id(3)] IReadOnlyList<EventEnvelope> Events,
    [property: Id(4)] IReadOnlyList<OutboxRecord> Effects,
    [property: Id(5)] DateTimeOffset CommittedAt);

[GenerateSerializer]
public sealed record V2CommitResult(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] bool Duplicate,
    [property: Id(2)] AggregateCommit Commit,
    [property: Id(3)] V2AggregateSnapshot Snapshot);
#pragma warning restore ORLEANS0010
