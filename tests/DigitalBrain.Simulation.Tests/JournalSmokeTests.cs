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

        // SessionNeuron.Activate() journals DigitalBrainActivated into its OWN Outgoing
        // journal BEFORE publishing it on the activation BroadcastChannel -- so the owner
        // session's own Outgoing journal is where the activation deterministically lands,
        // independent of whether any surface module subscribes. (Pin moved here in C2 Task 5
        // when the Brain absorbed the standalone DigitalBrainNeuron.)
        var subject = ISessionNeuron.ForOwner(brain.Owner);
        var delivery = await JournalWait.ForAsync(
            brain,
            subject,
            JournalKind.Outgoing,
            static d => d.Synapse is DigitalBrainActivated);

        Assert.IsType<DigitalBrainActivated>(delivery.Synapse);

        // The BroadcastChannel fan-out: the implicit channel subscriber
        // (surface-boot:{owner}/default, keyed by the channel key) journals the published
        // delivery through the regular Deliver path as its own Incoming.
        var surfaceBoot = new NeuronId("surface-boot", brain.Owner, "default");
        var received = await JournalWait.ForAsync(
            brain,
            surfaceBoot,
            JournalKind.Incoming,
            static d => d.Synapse is DigitalBrainActivated);

        Assert.Equal(delivery.SynapseId, received.SynapseId);
    }

    [Fact]
    public async Task JournalOverflowCompactsMidWait()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("compaction-owner"));
        var timerName = fixture.Sim.UniqueId("timer");
        var cancellationToken = TestContext.Current.CancellationToken;

        // CHOSEN SHAPE (documented per the task's discovery instruction): watch the SENDER's
        // own Outgoing journal (the owner's SessionNeuron), not the timer's Incoming journal.
        // A send journals into the sender's own Outgoing feed BEFORE the direct grain call to
        // the receiver, so watching the session's own Outgoing journal observes the overflow
        // as each fire commits, regardless of what the receiver does with the delivery.
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

        // 550 comfortably exceeds NeuronFeed's 512-entry retention cap. The assertion is
        // about the SENDER's own Outgoing journal, so StartTimer is used only because it is
        // the cheapest fireable synapse the Time module accepts. A fire is a direct awaited
        // call now: after the first one arms the timer, the handler REFUSES every further
        // StartTimer and that refusal surfaces to the sender -- but each send was already
        // journaled into the session's Outgoing feed before the call, which is all the
        // overflow needs, so the refusals are swallowed here. Fired concurrently to overlap
        // the round-trip latency.
        var fires = Enumerable.Range(0, 550).Select(async _ =>
        {
            try
            {
                await brain.FireAsync<TimerModule.ITimer>(
                    timerName,
                    new TimerModule.StartTimer(CommandId.New(), 60, "compaction-smoke"),
                    cancellationToken);
            }
            catch (NeuronAuthorizationException)
            {
            }
        });
        await Task.WhenAll(fires);

        var compacted = await Assert.ThrowsAsync<JournalCompactedException>(() => waitTask);
        Assert.Contains("mid-wait", compacted.Message, StringComparison.Ordinal);
    }
}
