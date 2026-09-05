using System.Reflection;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Product.Identity;
using Xunit;

namespace DigitalBrain.E2E.Tests;

public sealed class BrainGraphProjectionTests
{
    private static readonly OwnerId Owner = new("graph-owner");
    private static readonly ActorContext Actor = HttpActor.Current;
    private static readonly NeuronId Chat = ChatNamed("main");

    [Fact]
    public async Task Snapshot_uses_real_edges_and_never_walks_foreign_principals_or_owners()
    {
        var source = new TestSource();
        var related = ChatNamed("review");
        var foreignPrincipal = NeuronId.For<IChat>(Owner,
            PrincipalPartition.InstanceName(PrincipalId.New(), "private"));
        var foreignOwner = new NeuronId("chat", new OwnerId("another-owner"), Chat.Name);
        source.Set(Chat, [Edge(Chat, related), Edge(Chat, foreignPrincipal), Edge(Chat, foreignOwner)]);
        source.Set(IBrainNeuron.ForOwner(Owner), [Edge(IBrainNeuron.ForOwner(Owner), foreignPrincipal)]);

        var snapshot = await new BrainGraphProjection(source).ReadAsync("main", Actor, TestContext.Current.CancellationToken);

        var edge = Assert.Single(snapshot.Synapses);
        Assert.Equal(BrainGraphProjection.InstanceId(related), edge.TargetId);
        Assert.Equal("Bound", edge.Kind);
        Assert.True(edge.CanUnsubscribe);
        Assert.Contains(snapshot.Nodes, node => node.Id == "assistant:assistant" && node.Role == "participant");
        Assert.DoesNotContain(snapshot.Synapses, item => item.SourceId == "assistant:assistant" || item.TargetId == "assistant:assistant");
        Assert.DoesNotContain(foreignPrincipal, source.Queried);
        Assert.DoesNotContain(foreignOwner, source.Queried);
        Assert.Equal(BrainGraphProjection.SnapshotScope, snapshot.Scope);
    }

    [Fact]
    public async Task Journal_projection_uses_local_sequence_and_omits_payloads_and_other_principals()
    {
        var source = new TestSource();
        var secret = "a password, bearer token, document content, and exception detail";
        var lifecycle = new TurnLifecycle(TurnId.New(), CommandId.New(), Chat, ChatTurnStatus.Running, secret);
        var own = SignalDelivery.Create(lifecycle, Chat, 7, TimeProvider.System);
        var message = SignalDelivery.Create(new Note(secret), Chat, 8, TimeProvider.System);
        var foreign = SignalDelivery.Create(new Note(secret), Chat, 9, TimeProvider.System, principal: PrincipalId.New());
        source.Set(Chat, [], outgoing: new(200, [own, message, foreign], null));

        var snapshot = await new BrainGraphProjection(source).ReadAsync("main", Actor, TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.Activity.Count);
        Assert.Contains(snapshot.Activity, item => item.Sequence == 198 && item.SignalType == nameof(TurnLifecycle));
        Assert.Contains(snapshot.Activity, item => item.Sequence == 199 && item.PayloadPreview is null);
        Assert.Equal("Running", snapshot.Nodes.Single(node => node.Id == BrainGraphProjection.InstanceId(Chat)).Status);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subscription_changes_dispatch_to_target_and_preserve_verified_principal()
    {
        _ = Assembly.Load("DigitalBrain.Modules.UI");
        var source = new TestSource();
        var target = ChatNamed("review");
        source.Set(Chat, [Edge(Chat, target)]);
        var projection = new BrainGraphProjection(source);
        var request = new BrainGraphSubscriptionRequest(
            BrainGraphProjection.InstanceId(Chat), BrainGraphProjection.InstanceId(target), nameof(Note), false);

        var removed = await projection.SetSubscriptionAsync("main", Actor, request, TestContext.Current.CancellationToken);
        var first = Assert.Single(source.Sent);
        Assert.Equal(target, first.Target);
        Assert.Equal(Chat, Assert.IsType<Unsubscribe>(first.Signal).Source);
        Assert.Equal(Actor.PrincipalId, first.Principal);
        Assert.False(removed.Subscribed);

        var added = await projection.SetSubscriptionAsync("main", Actor, request with { Subscribed = true },
            TestContext.Current.CancellationToken);
        Assert.IsType<Subscribe>(source.Sent[1].Signal);
        Assert.True(added.Subscribed);
    }

    [Fact]
    public async Task Subscription_refuses_foreign_owner_unknown_target_unhandled_signal_and_learned_removal()
    {
        _ = Assembly.Load("DigitalBrain.Modules.UI");
        var source = new TestSource();
        var target = ChatNamed("review");
        source.Set(Chat, [Edge(Chat, target, SynapseKind.Learned)]);
        var projection = new BrainGraphProjection(source);
        var request = new BrainGraphSubscriptionRequest(
            BrainGraphProjection.InstanceId(Chat), BrainGraphProjection.InstanceId(target), nameof(Note), false);
        var requests = new[]
        {
            request,
            request with { SourceId = $"chat:another-owner/{Chat.Name}", Subscribed = true },
            request with { TargetId = BrainGraphProjection.InstanceId(ChatNamed("unobserved")), Subscribed = true },
            request with { SignalType = "DoesNotHandleThis", Subscribed = true },
            request with { SignalType = nameof(Subscribe), Subscribed = true },
            request with { TargetId = "assistant:assistant", Subscribed = true },
        };
        foreach (var invalid in requests)
        {
            await Assert.ThrowsAsync<NeuronAuthorizationException>(() => projection.SetSubscriptionAsync(
                "main", Actor, invalid, TestContext.Current.CancellationToken));
        }

        Assert.Empty(source.Sent);
    }

    [Fact]
    public async Task Removed_subscription_source_is_discoverable_from_retained_unsubscribe_event()
    {
        var source = new TestSource();
        var timer = new NeuronId("timer", Owner, "clock");
        source.Set(Chat, [], incoming: new(1,
            [SignalDelivery.Create(new Unsubscribe(timer, "Tick"), IBrainNeuron.ForOwner(Owner), 1,
                TimeProvider.System, principal: Actor.PrincipalId)], null));

        var snapshot = await new BrainGraphProjection(source).ReadAsync("main", Actor, TestContext.Current.CancellationToken);

        Assert.Contains(snapshot.Nodes, node => node.Id == "timer:clock");
        Assert.Empty(snapshot.Synapses);
        Assert.Contains(snapshot.Activity, item => item.Summary == "Subscription removed");
    }

    [Fact]
    public async Task Subscription_cannot_downgrade_an_innate_connection()
    {
        _ = Assembly.Load("DigitalBrain.Modules.UI");
        var source = new TestSource();
        var target = ChatNamed("review");
        source.Set(Chat, [Edge(Chat, target, SynapseKind.Innate)]);
        var request = new BrainGraphSubscriptionRequest(
            BrainGraphProjection.InstanceId(Chat), BrainGraphProjection.InstanceId(target), nameof(Note), true);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => new BrainGraphProjection(source)
            .SetSubscriptionAsync("main", Actor, request, TestContext.Current.CancellationToken));

        Assert.Empty(source.Sent);
    }

    [Fact]
    public async Task Snapshot_bounds_graph_traversal_without_fabricating_edges_to_omitted_nodes()
    {
        var source = new TestSource();
        source.Set(Chat, [.. Enumerable.Range(0, 30).Select(index => Edge(Chat, ChatNamed($"related-{index}")))]);

        var snapshot = await new BrainGraphProjection(source).ReadAsync("main", Actor, TestContext.Current.CancellationToken);

        Assert.True(snapshot.Truncated);
        Assert.Equal(BrainGraphProjection.MaxNodes, snapshot.Nodes.Count);
        Assert.Equal(BrainGraphProjection.MaxNodes, source.Queried.Count);
        Assert.All(snapshot.Synapses, edge => Assert.Contains(snapshot.Nodes, node => node.Id == edge.TargetId));
    }

    private static NeuronId ChatNamed(string name)
        => NeuronId.For<IChat>(Owner, PrincipalScoped.InstanceName(Actor.PrincipalId, name));

    private static Synapse Edge(NeuronId from, NeuronId to, SynapseKind kind = SynapseKind.Bound)
        => new(from, to, nameof(Note), 1, DateTimeOffset.UtcNow, kind, 3);

    private sealed class TestSource : IBrainGraphSource
    {
        private readonly Dictionary<NeuronId, BrainGraphNeuronRead> _reads = [];

        public OwnerId Owner => BrainGraphProjectionTests.Owner;
        public List<NeuronId> Queried { get; } = [];
        public List<(NeuronId Target, Signal Signal, PrincipalId? Principal)> Sent { get; } = [];

        public void Set(NeuronId neuron, IReadOnlyList<Synapse> synapses,
            JournalRead? incoming = null, JournalRead? outgoing = null)
            => _reads[neuron] = new(synapses, incoming ?? new(0, [], null), outgoing ?? new(0, [], null));

        public Task<NeuronId?> ReadActiveExecutionAsync(NeuronId chat, CancellationToken cancellationToken)
            => Task.FromResult<NeuronId?>(null);

        public Task<BrainGraphNeuronRead> ReadAsync(NeuronId neuron, CancellationToken cancellationToken)
        {
            Queried.Add(neuron);
            return Task.FromResult(_reads.GetValueOrDefault(neuron) ?? new([], new(0, [], null), new(0, [], null)));
        }

        public Task<DeliveryOutcome> SendAsync(NeuronId receiver, Signal signal, CancellationToken cancellationToken)
        {
            Sent.Add((receiver, signal, VerifiedActor.Current?.PrincipalId));
            return Task.FromResult(DeliveryOutcome.Handled);
        }
    }
}
