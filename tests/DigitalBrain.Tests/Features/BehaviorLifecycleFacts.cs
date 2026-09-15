using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using DigitalBrain.Core.Behaviors;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorLifecycleFacts
{
    private static readonly NeuronId BehaviorId = new("behavior", "lifecycle");
    private const string Schema = """{"type":"object","additionalProperties":false}""";

    [Fact]
    public async Task Pending_lifecycle_work_rejects_conflicting_saves_and_preserves_expected_version()
    {
        var gate = new LifecycleDrainGate { Paused = true };
        await using var brain = await Start(gate);
        var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
        var original = Definition("original");
        var save = new SaveBehavior(original, CommandId.New(), 0);
        var accepted = await behavior.Save(save);
        Assert.Equal(accepted, await behavior.Save(save));
        await Assert.ThrowsAsync<CommandRejectedException>(() => behavior.Save(new(Definition("overwrite"), CommandId.New(), 0)));
        Assert.Equal(0, (await behavior.Read()).Version);
        await Drain(brain, gate);
        Assert.Equal("original", (await behavior.Read()).Definition!.Name);
        Assert.Equal(1, (await behavior.Read()).Version);
        await Assert.ThrowsAsync<CommandRejectedException>(() => behavior.Save(new(Definition("stale"), CommandId.New(), 0)));

        gate.Paused = true;
        await behavior.Start(new(CommandId.New(), 1));
        await Assert.ThrowsAsync<CommandRejectedException>(() => behavior.Save(new(Definition("silently-lost"), CommandId.New(), 1)));
        await Drain(brain, gate);
        Assert.Equal(BehaviorStatus.Running, (await behavior.Read()).Status);
        Assert.Equal("original", (await behavior.Read()).Definition!.Name);
        await behavior.Stop(new(CommandId.New(), 1));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Stopped, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancelled_work_releases_its_reservation_before_the_next_command()
    {
        var gate = new LifecycleDrainGate { Paused = true };
        await using var brain = await Start(gate);
        var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
        var accepted = await behavior.Save(new(Definition("cancelled"), CommandId.New(), 0));
        await brain.Grains.GetGrain<INeuron>(BehaviorId.ToGrainId()).CancelReaction(accepted.Work);
        await Drain(brain, gate);
        Assert.Null((await behavior.Read()).Definition);
        await behavior.Save(new(Definition("replacement"), CommandId.New(), 0));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Version == 1, TestContext.Current.CancellationToken);
        Assert.Equal("replacement", (await behavior.Read()).Definition!.Name);
    }

    [Fact]
    public async Task Failed_claim_rolls_back_owned_nodes_without_disabling_the_foreign_node()
    {
        var gate = new LifecycleDrainGate();
        await using var brain = await Start(gate);
        var behavior = brain.Grains.GetGrain<IBehavior>(BehaviorId.ToGrainId());
        var definition = Definition("collision");
        await behavior.Save(new(definition, CommandId.New()));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Version == 1, TestContext.Current.CancellationToken);
        gate.Paused = true;
        var start = await behavior.Start(new(CommandId.New()));
        var first = new NeuronId("behavior-map", BehaviorMapping.OwnedName(BehaviorId, start.Work.ToString(), "first"));
        var second = new NeuronId("behavior-map", BehaviorMapping.OwnedName(BehaviorId, start.Work.ToString(), "second"));
        var foreignOwner = brain.Grains.GetGrain<INeuronOwnership>(second.ToGrainId());
        var foreignProcessor = brain.Grains.GetGrain<IBehaviorNode>(second.ToGrainId());
        await foreignOwner.Claim("foreign-run");
        await foreignProcessor.Configure(new("foreign-run", definition.Nodes[2], true));
        await Drain(brain, gate);
        var stopped = await behavior.Read();
        Assert.Equal(BehaviorStatus.Stopped, stopped.Status);
        Assert.NotNull(stopped.Error);
        Assert.True((await foreignProcessor.Status()).Enabled);
        Assert.False((await brain.Grains.GetGrain<IBehaviorNode>(first.ToGrainId()).Status()).Enabled);
        await brain.Grains.GetGrain<INeuronOwnership>(first.ToGrainId()).Claim("replacement-owner");
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreignOwner.Claim("replacement-owner"));
        await behavior.Stop(new(CommandId.New()));
        await ReactionWait.UntilAsync(async () => await brain.Grains.GetGrain<INeuron>(BehaviorId.ToGrainId()).ReadPendingCount() == 0, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Configuration_requires_a_claim_and_cannot_replace_a_run_definition()
    {
        await using var brain = await Start(new());
        var id = new NeuronId("behavior-map", "immutable-config");
        var owner = brain.Grains.GetGrain<INeuronOwnership>(id.ToGrainId());
        var processor = brain.Grains.GetGrain<IBehaviorNode>(id.ToGrainId());
        var node = Definition("test").Nodes[1];
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.Configure(new("run", node, true)));
        await owner.Claim("run");
        await processor.Configure(new("run", node, true));
        var changed = node with { Configuration = """{"template":{"injected":true}}""" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.Configure(new("run", changed, false)));
        Assert.True((await processor.Status()).Enabled);
        await processor.Configure(new("run", node with { }, false));
        await processor.Configure(new("run", node with { }, true));
        await owner.Release("run");
        await owner.Claim("foreign-run");
        await processor.Configure(new("run", changed, false));
        Assert.True((await processor.Status()).Enabled);
    }

    private static BehaviorDefinition Definition(string name) => new(name,
        [new("source", "source", "{}", Output: new("Note", Schema), SharedNeuron: NeuronId.Plain("lifecycle-source")),
         new("first", "map", """{"template":{}}""", new("Note", Schema), new("Note", Schema)),
         new("second", "map", """{"template":{}}""", new("Note", Schema), new("Note", Schema))],
        [new("source", "first"), new("first", "second")]);

    private static Task<BrainSimulation> Start(LifecycleDrainGate gate) => BrainSimulation.StartAsync(new()
    {
        Modules = new([]),
        ConfigureSilo = silo => silo.Services.AddSingleton<IIncomingGrainCallFilter>(gate)
    });

    private static async Task Drain(BrainSimulation brain, LifecycleDrainGate gate)
    {
        gate.Paused = false;
        await brain.Grains.GetGrain<INeuronInbox>(BehaviorId.ToGrainId()).Drain();
        await ReactionWait.UntilAsync(async () => await brain.Grains.GetGrain<INeuron>(BehaviorId.ToGrainId()).ReadPendingCount() == 0, TestContext.Current.CancellationToken);
    }

    private sealed class LifecycleDrainGate : IIncomingGrainCallFilter
    {
        private volatile bool _paused;
        public bool Paused { get => _paused; set => _paused = value; }
        public Task Invoke(IIncomingGrainCallContext context) => Paused && context.InterfaceMethod.DeclaringType == typeof(INeuronInbox) && context.Grain is BehaviorNeuron
            ? Task.CompletedTask : context.Invoke();
    }
}

