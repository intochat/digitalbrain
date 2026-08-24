using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using Orleans.Runtime;

namespace DigitalBrain.SmartPrompt;

[GenerateSerializer]
internal sealed record BehaviorIngressState([property: Id(0)] List<string> EventIds);

[GenerateSerializer]
internal sealed record BehaviorDirectoryState(
    [property: Id(0)] HashSet<int> ActivePartitions,
    [property: Id(1)] int SubscriptionCount);

[GenerateSerializer]
internal sealed record BehaviorPartitionState([property: Id(0)] List<BehaviorSubscription> Subscriptions);

[GenerateSerializer]
internal sealed record BehaviorRunnerState([property: Id(0)] List<string> CompletedEventIds);

[GrainType("behavior-ingress")]
internal sealed class BehaviorIngress(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorIngressState> state)
    : Grain, IBehaviorIngress
{
    private const int RetainedEventIds = 4096;

    public async Task Publish(BehaviorEvent behaviorEvent)
    {
        Validate(behaviorEvent);
        var current = state.RecordExists ? state.State : new BehaviorIngressState([]);
        var deduplicationKey = $"{behaviorEvent.Kind}:{behaviorEvent.EventId}";
        if (current.EventIds.Contains(deduplicationKey, StringComparer.Ordinal))
        {
            return;
        }

        await GrainFactory.GetGrain<IBehaviorTriggerDirectory>(behaviorEvent.TriggerKey).Publish(behaviorEvent);
        current.EventIds.Add(deduplicationKey);
        while (current.EventIds.Count > RetainedEventIds)
        {
            current.EventIds.RemoveAt(0);
        }
        state.State = current;
        await state.WriteStateAsync();
    }

    private static void Validate(BehaviorEvent behaviorEvent)
    {
        ArgumentNullException.ThrowIfNull(behaviorEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorEvent.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorEvent.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorEvent.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorEvent.SourceUri);
    }
}

[GrainType("behavior-trigger-directory")]
internal sealed class BehaviorTriggerDirectory(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorDirectoryState> state)
    : Grain, IBehaviorTriggerDirectory
{
    public async Task Subscribe(BehaviorSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var partition = BehaviorPartition.For(subscription);
        var added = await Partition(partition).Subscribe(subscription);
        if (!added)
        {
            return;
        }
        var current = Current();
        current.ActivePartitions.Add(partition);
        state.State = current with { SubscriptionCount = current.SubscriptionCount + 1 };
        await state.WriteStateAsync();
    }

    public async Task Unsubscribe(BehaviorSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var partition = BehaviorPartition.For(subscription);
        var removed = await Partition(partition).Unsubscribe(subscription);
        if (!removed)
        {
            return;
        }
        var current = Current();
        state.State = current with { SubscriptionCount = Math.Max(0, current.SubscriptionCount - 1) };
        await state.WriteStateAsync();
    }

    public Task Publish(BehaviorEvent behaviorEvent)
    {
        ArgumentNullException.ThrowIfNull(behaviorEvent);
        return Task.WhenAll(Current().ActivePartitions.Select(partition => Partition(partition).Publish(behaviorEvent)));
    }

    public Task<BehaviorDirectoryStats> ReadStats()
    {
        var current = Current();
        return Task.FromResult(new BehaviorDirectoryStats(
            current.SubscriptionCount,
            current.ActivePartitions.Count,
            BehaviorRouting.PartitionCount));
    }

    private BehaviorDirectoryState Current()
        => state.RecordExists ? state.State : new BehaviorDirectoryState([], 0);

    private IBehaviorSubscriptionPartition Partition(int partition)
        => GrainFactory.GetGrain<IBehaviorSubscriptionPartition>($"{this.GetPrimaryKeyString()}::p:{partition:D2}");
}

[GrainType("behavior-subscription-partition")]
internal sealed class BehaviorSubscriptionPartition(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorPartitionState> state)
    : Grain, IBehaviorSubscriptionPartition
{
    public async Task<bool> Subscribe(BehaviorSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var current = Current();
        if (current.Subscriptions.Contains(subscription))
        {
            return false;
        }
        current.Subscriptions.Add(subscription);
        state.State = current;
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> Unsubscribe(BehaviorSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var current = Current();
        if (!current.Subscriptions.Remove(subscription))
        {
            return false;
        }
        state.State = current;
        await state.WriteStateAsync();
        return true;
    }

    public Task Publish(BehaviorEvent behaviorEvent)
    {
        ArgumentNullException.ThrowIfNull(behaviorEvent);
        return Task.WhenAll(Current().Subscriptions.Select(subscription =>
            GrainFactory.GetGrain<IBehaviorRunner>(BehaviorRunnerKey.For(subscription))
                .Deliver(subscription, behaviorEvent)));
    }

    private BehaviorPartitionState Current()
        => state.RecordExists ? state.State : new BehaviorPartitionState([]);
}

internal static class BehaviorPartition
{
    public static int For(BehaviorSubscription subscription)
    {
        var value = $"{subscription.Owner}\n{subscription.BehaviorName}\n{subscription.ScenarioName}\n{subscription.RevisionHash}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return (int)(BitConverter.ToUInt32(hash) % BehaviorRouting.PartitionCount);
    }
}

internal static class BehaviorRunnerKey
{
    public static string For(BehaviorSubscription subscription)
        => $"{subscription.Owner}/{subscription.BehaviorName}@{subscription.RevisionHash}:{subscription.ScenarioName}";
}
