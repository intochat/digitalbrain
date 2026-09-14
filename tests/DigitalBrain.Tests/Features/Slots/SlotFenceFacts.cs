using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// Design 4.4: a silo whose slot does not hold the lease answers reads, refuses commands, ignores reminder
// ticks and does not drain. The pending work it was holding is drained by whoever holds the lease next.
public sealed class SlotFenceFacts
{
    private static Task<BrainSimulation> StartAsync(InMemoryActiveSlotLease lease) => BrainSimulation.StartAsync(new()
    {
        Modules = new([]),
        Configuration = new Dictionary<string, string?> { ["DigitalBrain:Slot"] = lease.Slot },
        ConfigureSilo = silo =>
        {
            silo.Services.AddSingleton(new CounterFixtureState());
            silo.Services.AddSingleton<IActiveSlotLease>(lease);
        },
    });

    private static ICounter Counter(BrainSimulation brain, string name)
        => brain.Grains.GetGrain<ICounter>(new NeuronId("counter", name).ToGrainId());

    [Fact]
    public async Task A_command_on_a_standby_slot_is_refused_and_nothing_is_journaled()
    {
        var lease = new InMemoryActiveSlotLease("a", holder: "other");
        await using var brain = await StartAsync(lease);
        var counter = Counter(brain, "fenced");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => counter.Add(new AddCount(CommandId.New(), 1)));
        Assert.Contains("standby slot", error.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", error.Message, StringComparison.Ordinal);

        // The refusal is transient, so it must not enter the command journal: the same command id has to
        // work after the promotion, and a journaled rejection would replay as a rejection forever.
        Assert.Empty((await counter.ReadCommands(0)).Delta);
    }

    [Fact]
    public async Task A_read_answers_on_a_standby_slot()
    {
        var lease = new InMemoryActiveSlotLease("a", holder: "other");
        await using var brain = await StartAsync(lease);
        var counter = Counter(brain, "readable");

        // The promotion's smoke check is exactly this: a [ReadOnly] read against a fenced silo.
        Assert.Equal(0, await counter.ReadTotal());
        Assert.Empty(await counter.ReadSynapses());
        Assert.Equal(0, await counter.ReadPendingCount());
    }

    [Fact]
    public async Task A_standby_slot_neither_drains_nor_wakes_until_it_holds_the_lease()
    {
        var lease = new InMemoryActiveSlotLease("a");
        await using var brain = await StartAsync(lease);
        // Every reaction fails while this counter is above zero, so the pending head stays and each attempt
        // is counted: that is how a fact can see a drain happen, or not happen.
        FixtureSwitches.FlakyFailuresLeft["fenced"] = 50;
        var flaky = new NeuronId("flaky", "fenced");
        var source = brain.Grains.GetGrain<INeuron>(NeuronId.Plain("caller").ToGrainId());
        await source.Connect(flaky, "Ping");
        await source.Fire(Signal.Create("Ping", "{}"), null, null, TestContext.Current.CancellationToken);
        await TestWait.UntilAsync(() => Task.FromResult(Attempts()), attempts => attempts >= 1,
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var before = Attempts();
        lease.Holder = "other";
        var inbox = brain.Grains.GetGrain<INeuronInbox>(flaky.ToGrainId());
        await inbox.Drain();
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var neuron = brain.Grains.GetGrain<INeuron>(flaky.ToGrainId());
        Assert.Equal(before, Attempts());
        Assert.Equal(1, await neuron.ReadPendingCount());

        lease.Holder = "a";
        await inbox.Drain();
        var after = await TestWait.UntilAsync(() => Task.FromResult(Attempts()), attempts => attempts > before,
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(after > before, $"the promoted slot drained {after} times, the standby {before}");
        FixtureSwitches.FlakyFailuresLeft.TryRemove("fenced", out _);
    }

    private static int Attempts() => FixtureSwitches.Reactions
        .Where(entry => entry.Key.StartsWith("fenced:", StringComparison.Ordinal))
        .Sum(entry => entry.Value);
}
