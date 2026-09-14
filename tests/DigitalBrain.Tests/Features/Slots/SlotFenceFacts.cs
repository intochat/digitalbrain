using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// Design 4.4: a silo whose slot does not hold the lease answers reads, refuses commands, ignores reminder
// ticks and does not drain. The pending work it was holding is drained by whoever holds the lease next.
public sealed class SlotFenceFacts
{
    private static Task<BrainSimulation> StartAsync(InMemoryActiveSlotLease lease, string? persistenceDirectory = null) => BrainSimulation.StartAsync(new()
    {
        Modules = new([]),
        Configuration = new Dictionary<string, string?> { ["DigitalBrain:Slot"] = lease.Slot },
        PersistenceDirectory = persistenceDirectory,
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

        var error = await Assert.ThrowsAsync<StandbySlotException>(() => counter.Add(new AddCount(CommandId.New(), 1)));
        Assert.Equal("a", error.Slot);
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
        await TestWait.UntilAsync(() => Task.FromResult(Attempts("fenced")), attempts => attempts >= 1,
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var before = Attempts("fenced");
        lease.Holder = "other";
        var inbox = brain.Grains.GetGrain<INeuronInbox>(flaky.ToGrainId());
        await inbox.Drain();
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var neuron = brain.Grains.GetGrain<INeuron>(flaky.ToGrainId());
        Assert.Equal(before, Attempts("fenced"));
        Assert.Equal(1, await neuron.ReadPendingCount());

        // No natural reminder tick lands inside this window (the retry period is seconds, the reminder
        // service polls on a much longer cadence), so call the reminder entry point directly, the way the
        // reminder subsystem would: it must respect the same fence as Drain.
        var remindable = brain.Grains.GetGrain<IRemindable>(flaky.ToGrainId());
        await remindable.ReceiveReminder(RetryScheduler.ReminderName, default);
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(before, Attempts("fenced"));
        Assert.Equal(1, await neuron.ReadPendingCount());

        lease.Holder = "a";
        await remindable.ReceiveReminder(RetryScheduler.ReminderName, default);
        var after = await TestWait.UntilAsync(() => Task.FromResult(Attempts("fenced")), attempts => attempts > before,
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(after > before, $"the promoted slot drained {after} times, the standby {before}");
        FixtureSwitches.FlakyFailuresLeft.TryRemove("fenced", out _);
    }

    [Fact]
    public async Task A_standby_slots_activation_answers_a_read_without_rewriting_the_reminder_row()
    {
        var persistenceDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        var lease = new InMemoryActiveSlotLease("a");
        await using var brain = await StartAsync(lease, persistenceDirectory);

        // Seed pending work while this slot holds the lease: Deliver registers the retry reminder as part
        // of admitting it, before either slot's activation logic runs at all.
        FixtureSwitches.FlakyFailuresLeft["fresh"] = 50;
        var flaky = new NeuronId("flaky", "fresh");
        var source = brain.Grains.GetGrain<INeuron>(NeuronId.Plain("caller").ToGrainId());
        await source.Connect(flaky, "Ping");
        await source.Fire(Signal.Create("Ping", "{}"), null, null, TestContext.Current.CancellationToken);
        await TestWait.UntilAsync(() => Task.FromResult(Attempts("fresh")), attempts => attempts >= 1,
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var reminders = brain.SiloServices.GetRequiredService<IReminderTable>();
        var registered = await reminders.ReadRow(flaky.ToGrainId(), RetryScheduler.ReminderName);
        Assert.NotNull(registered);

        // Flip to standby and force a fresh OnActivateAsync with a cold restart: file-backed grain
        // storage and the reminder row both survive it, so only this activation could change the row.
        lease.Holder = "other";
        await brain.RestartSiloAsync(TestContext.Current.CancellationToken);

        var neuron = brain.Grains.GetGrain<INeuron>(flaky.ToGrainId());
        Assert.Equal(1, await neuron.ReadPendingCount());

        var remindersAfterRestart = brain.SiloServices.GetRequiredService<IReminderTable>();
        var afterStandbyRead = await remindersAfterRestart.ReadRow(flaky.ToGrainId(), RetryScheduler.ReminderName);
        Assert.NotNull(afterStandbyRead);
        Assert.Equal(registered!.ETag, afterStandbyRead!.ETag);
        FixtureSwitches.FlakyFailuresLeft.TryRemove("fresh", out _);
    }

    private static int Attempts(string name) => FixtureSwitches.Reactions
        .Where(entry => entry.Key.StartsWith(name + ":", StringComparison.Ordinal))
        .Sum(entry => entry.Value);
}
