using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Behaviors.Runtime;

internal interface IBehaviorSubscriptionRegistry : IGrainWithStringKey
{
    [Alias(nameof(Replace))]
    Task Replace(string behaviorName, IReadOnlyList<string> eventAliases, CancellationToken cancellationToken);

    // Every owner-scoped broadcast reads this grain, so the read must not queue behind a
    // behavior's activation write. It loads and looks up; it never mutates.
    [AlwaysInterleave]
    [Alias(nameof(SubscribersOf))]
    Task<IReadOnlyList<string>> SubscribersOf(string eventAlias, CancellationToken cancellationToken);
}

internal static class BehaviorSubscriptionRegistry
{
    internal const string GrainTypeName = "behavior-subscriptions";
    internal const string InstanceName = "registry";

    internal static NeuronId ForOwner(OwnerId owner) => new(GrainTypeName, owner, InstanceName);

    // Replace is a serialized write on a grain every behavior in the owner contends for, so a
    // stalled registry would otherwise hang activation itself. Bounded, and loud on expiry: an
    // activation that proceeds unpublished is exactly the silently-deaf divergence this repairs.
    internal static async Task WithinBoundAsync(
        Func<CancellationToken, Task> registryCall,
        string operation,
        TimeSpan bound,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registryCall);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(bound);

        try
        {
            await registryCall(bounded.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Behavior subscription registry did not complete '{operation}' within {bound}.");
        }
    }
}

[GrainType(BehaviorSubscriptionRegistry.GrainTypeName)]
internal sealed class BehaviorSubscriptionRegistryGrain : DurableGrain, IBehaviorSubscriptionRegistry
{
    private const string StateName = "behaviors.subscriptions";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<BehaviorSubscriptionData> _states;

    public BehaviorSubscriptionRegistryGrain()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<BehaviorSubscriptionData>>();
    }

    public async Task Replace(
        string behaviorName,
        IReadOnlyList<string> eventAliases,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorName);
        ArgumentNullException.ThrowIfNull(eventAliases);

        var data = Load();
        var next = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (alias, subscribers) in data.ByAlias)
        {
            var remaining = subscribers
                .Where(name => !string.Equals(name, behaviorName, StringComparison.Ordinal))
                .ToList();
            if (remaining.Count > 0)
            {
                next[alias] = remaining;
            }
        }

        foreach (var alias in eventAliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new ArgumentException("A behavior event alias cannot be blank.", nameof(eventAliases));
            }

            if (!next.TryGetValue(alias, out var subscribers))
            {
                subscribers = [];
                next[alias] = subscribers;
            }

            subscribers.Add(behaviorName);
            subscribers.Sort(StringComparer.Ordinal);
        }

        _state.Value = _states.SerializeToArray(new BehaviorSubscriptionData { ByAlias = next });
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);
    }

    public Task<IReadOnlyList<string>> SubscribersOf(string eventAlias, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(eventAlias);

        return Task.FromResult<IReadOnlyList<string>>(
            Load().ByAlias.TryGetValue(eventAlias, out var subscribers) ? subscribers : []);
    }

    private BehaviorSubscriptionData Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : new BehaviorSubscriptionData();

    [GenerateSerializer]
    internal sealed record BehaviorSubscriptionData
    {
        [Id(0)]
        public Dictionary<string, List<string>> ByAlias { get; init; } = new(StringComparer.Ordinal);
    }
}

internal sealed class BehaviorBroadcastSubscribers : IBroadcastSubscribers
{
    private static readonly string BehaviorGrainType =
        NeuronId.GrainTypeNameOf(typeof(IBehaviorNeuron));

    private readonly IGrainFactory _grains;

    public BehaviorBroadcastSubscribers(IGrainFactory grains)
    {
        ArgumentNullException.ThrowIfNull(grains);
        _grains = grains;
    }

    public async ValueTask<IReadOnlyCollection<NeuronId>> ReceiversFor(
        OwnerId owner,
        string eventAlias,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventAlias);

        var registry = _grains.GetGrain<IBehaviorSubscriptionRegistry>(
            BehaviorSubscriptionRegistry.ForOwner(owner).ToGrainId());
        var subscribers = await registry.SubscribersOf(eventAlias, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return subscribers.Count == 0
            ? []
            : subscribers.Select(name => new NeuronId(BehaviorGrainType, owner, name)).ToArray();
    }
}
