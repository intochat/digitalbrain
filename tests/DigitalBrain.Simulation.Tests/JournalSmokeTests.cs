using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Testing;
using Xunit;
using TimerModule = DigitalBrain.Time;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class JournalSmokeTests(SimulationFixture fixture)
{
    [Fact]
    public async Task ActivationLandsInTheSessionJournal()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("journal-owner"));
        await brain.ActivateAsync(TestContext.Current.CancellationToken);

        // DigitalBrainNeuron.Activate() sends DigitalBrainActivated to the surface-boot
        // instance via Directed SendAsync, not Emit. NeuronMessagePipeline.FireAsync stages
        // that delivery into the SENDER's own Outgoing journal (turn.StageOutgoing) BEFORE the
        // outbox even attempts to route it to the receiver -- so IDigitalBrainNeuron's own
        // Outgoing journal is where the activation deterministically lands, independent of
        // whether the UI module (surface-boot's grain type) is loaded by this fixture's
        // Time-only ModuleAssemblies.
        var subject = IDigitalBrainNeuron.ForOwner(brain.Owner);
        var delivery = await JournalWait.ForAsync(
            brain,
            subject,
            JournalKind.Outgoing,
            static d => d.Synapse is DigitalBrainActivated);

        Assert.IsType<DigitalBrainActivated>(delivery.Synapse);
    }

    [Fact]
    public async Task JournalOverflowCompactsMidWait()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("compaction-owner"));
        var timerName = fixture.Sim.UniqueId("timer");
        var timerSubject = NeuronId.For<TimerModule.ITimer>(brain.Owner, timerName);
        var cancellationToken = TestContext.Current.CancellationToken;

        // CHOSEN SHAPE (documented per the task's discovery instruction): watch the SENDER's
        // own Outgoing journal (the owner's SessionNeuron), not the timer's Incoming journal.
        // An earlier version fired at the timer and watched ITS Incoming feed; overflowing that
        // requires 550 deliveries to actually drain through SessionNeuron's outbox to a single
        // contended receiver, which measured anywhere from ~40s to 3m45s+ across runs -- too
        // slow and too timing-variable to race deterministically within a test timeout.
        // NeuronMessagePipeline.FireAsync appends every fired synapse to the SENDER's own
        // Outgoing feed SYNCHRONOUSLY (turn.StageOutgoing, before the outbox even attempts to
        // route it to a receiver), so watching the session's own Outgoing journal observes the
        // overflow the instant each FireAsync call commits, decoupled entirely from how slowly
        // (or whether) the receiver ever actually processes the deliveries.
        var subject = ISessionNeuron.ForOwner(brain.Owner);

        // Start the wait BEFORE firing anything. `JournalWait.ForAsync` is a plain async
        // method, so it runs synchronously up to its first `await brain.ReadJournalAsync(...)`
        // before control returns here -- that first read is already in flight against an empty
        // journal before the fire loop below issues its first call. Its `isBaselineRead` flag
        // flips to false unconditionally after that first iteration (see JournalWait), so once
        // retention is exceeded and a LATER poll observes a ResetSnapshot, it is treated as a
        // genuine mid-wait compaction, not the wait's starting baseline.
        var waitTask = JournalWait.ForAsync(
            brain,
            subject,
            JournalKind.Outgoing,
            static _ => false,
            timeout: TimeSpan.FromSeconds(45));

        // 550 comfortably exceeds NeuronFeed's 512-entry retention cap. The receiver (the
        // fixture's real ITimer neuron) never needs to actually process these for this test --
        // the assertion is about the SENDER's own Outgoing journal, so StartTimer is used only
        // because it is the cheapest fireable synapse the Time module accepts. Fired
        // concurrently, not one at a time: each FireAsync round-trips twice (ActivateAsync then
        // Session().Fire), and dispatching all 550 at once overlaps that round-trip latency
        // instead of paying it 1100 times sequentially.
        var fires = Enumerable.Range(0, 550).Select(_ => brain.FireAsync(
            timerSubject,
            new TimerModule.StartTimer(CommandId.New(), 60, "compaction-smoke"),
            cancellationToken));
        await Task.WhenAll(fires);

        var compacted = await Assert.ThrowsAsync<JournalCompactedException>(() => waitTask);
        Assert.Contains("mid-wait", compacted.Message, StringComparison.Ordinal);
    }
}
