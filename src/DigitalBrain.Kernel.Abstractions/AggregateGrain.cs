using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using Orleans;
using System.Text.Json;

namespace DigitalBrain.Kernel.Runtime;

[Alias("digitalbrain.v2.aggregate-grain")]
public interface IAggregateGrain : IGrainWithStringKey
{
    [Alias("v2.read")]
    Task<V2AggregateSnapshot> ReadAsync();
    [Alias("v2.commit")]
    Task<V2CommitResult> CommitAsync(V2CommitRequest request);
    [Alias("v2.effect-transition")]
    Task AppendEffectTransitionAsync(EffectTransitionRecord transition);
    [Alias("v2.try-effect-transition")]
    Task<bool> TryAppendEffectTransitionAsync(string effectId, string? expectedTransitionId, EffectTransitionRecord transition);
}

[Alias("digitalbrain.v2.effect-worker-grain")]
public interface IEffectWorkerGrain : IGrainWithStringKey
{
    [Alias("v2.execute-effect")]
    Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration);
}

/// <summary>Application-facing effect execution port; transport adapters never resolve grains directly.</summary>
public interface IEffectWorkerPort
{
    Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
}

/// <summary>Orleans client adapter for the application-facing effect port.</summary>
public sealed class OrleansClientEffectWorkerPort(IClusterClient cluster) : IEffectWorkerPort
{
    public Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return cluster.GetGrain<IEffectWorkerGrain>(aggregateId)
            .ExecuteAsync(aggregateId, effectId, leaseOwner, leaseDuration);
    }
}

[GenerateSerializer, Alias("digitalbrain.v2.aggregate-grain-state")]
public sealed class AggregateGrainState
{
    [Id(0)] public long CommitSequence { get; set; }
    [Id(1)] public JsonElement State { get; set; } = JsonDocument.Parse("null").RootElement.Clone();
    [Id(2)] public List<AggregateCommit> Commits { get; set; } = [];
    [Id(3)] public List<OutboxRecord> Outbox { get; set; } = [];
    [Id(4)] public List<EffectTransitionRecord> EffectTransitions { get; set; } = [];
    [Id(5)] public List<V2InboxRecord> Inbox { get; set; } = [];

    public V2AggregateSnapshot Snapshot() => new(CommitSequence, State.Clone(), Commits.ToArray(), Outbox.ToArray(), EffectTransitions.ToArray(), Inbox.ToArray());

    public static AggregateGrainState FromSnapshot(V2AggregateSnapshot snapshot) => new()
    {
        CommitSequence = snapshot.CommitSequence,
        State = snapshot.State.Clone(),
        Commits = snapshot.Commits.ToList(),
        Outbox = snapshot.Outbox.ToList(),
        EffectTransitions = snapshot.EffectTransitions.ToList(),
        Inbox = snapshot.Inbox.ToList()
    };
}
