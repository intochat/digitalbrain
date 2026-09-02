using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Memory;
using DigitalBrain.Testing;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class JournalSmokeTests(SimulationFixture fixture)
{
    [Fact]
    public void HistoricalFactJournalAliasesRemainResolvable()
    {
        // Chat used to journal these signals. They are no longer emitted, but existing
        // retained journals must still be readable after the fact-memory projection is gone.
        Assert.Equal("memory.store-fact", AliasOf<StoreFact>());
        Assert.Equal("memory.fact-stored", AliasOf<FactStored>());
    }

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
            static d => d.Signal is DigitalBrainActivated);

        Assert.IsType<DigitalBrainActivated>(delivery.Signal);

        // The BroadcastChannel fan-out: the implicit channel subscriber
        // (surface-boot:{owner}/default, keyed by the channel key) journals the published
        // delivery through the regular Deliver path as its own Incoming.
        var surfaceBoot = new NeuronId("surface-boot", brain.Owner, "default");
        var received = await JournalWait.ForAsync(
            brain,
            surfaceBoot,
            JournalKind.Incoming,
            static d => d.Signal is DigitalBrainActivated);

        Assert.Equal(delivery.SignalId, received.SignalId);
    }

    private static string AliasOf<T>()
        => typeof(T).GetCustomAttributes(typeof(AliasAttribute), inherit: false)
            .OfType<AliasAttribute>()
            .Single()
            .Alias;
}
