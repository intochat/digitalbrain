using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Brain;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Client;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class WireDeliveryTests(SimulationFixture fixture)
{
    // The C2 review's named gap: nothing anywhere wired a connection and observed the
    // emission land. This is the end-to-end delivery path a brain wire promises:
    // Connect -> source EmitAsync -> Brain.Route -> target Deliver -> target Incoming journal.
    [Fact]
    public async Task AConnectedWireDeliversTheEmissionToTheTargetsIncomingJournal()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("wire-owner"));
        var cancellationToken = TestContext.Current.CancellationToken;
        var pinger = NeuronId.For<IPingerNeuron>(brain.Owner, fixture.Sim.UniqueId("pinger"));
        var echo = NeuronId.For<IEchoNeuron>(brain.Owner, fixture.Sim.UniqueId("echo"));
        var grain = fixture.Sim.Grains.GetGrain<IBrain>(
            EntityId.For<IBrain>(brain.Owner, DigitalBrainNames.DefaultBrain).ToGrainId());

        await grain.Connect(new Connection(pinger, Pinged.AliasName, echo));
        await brain.FireAsync<IPingerNeuron>(pinger.Name, new EmitPing("across-the-wire"), cancellationToken);

        var delivered = await JournalWait.ForAsync(
            brain,
            echo,
            JournalKind.Incoming,
            static d => d.Synapse is Pinged { Note: "across-the-wire" });

        Assert.IsType<Pinged>(delivered.Synapse);
    }
}
