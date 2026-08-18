using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class EntityTests(SimulationFixture fixture)
{
    [Fact]
    public async Task EntityRoundTripsState()
    {
        var name = fixture.Sim.UniqueId("counter");
        var counter = fixture.Sim.Brain.GetEntity<ICounterEntity>(name);
        await counter.Add(2);
        await counter.Add(3);
        var state = await counter.Read();
        Assert.Equal(5, state!.Total);
    }

    [Fact]
    public async Task CrossOwnerEntityCallIsWalled()
    {
        var name = fixture.Sim.UniqueId("walled");
        await fixture.Sim.Brain.GetEntity<ICounterEntity>(name).Add(1);
        var firstOwner = fixture.Sim.Brain.Owner;

        // Two things were verified empirically before settling on this shape (see
        // ICounterEntity.ReachAcrossOwner's comment for the full discovery): neither
        // DigitalBrainClient.GetEntity(name) under BrainFor(stranger) NOR a raw
        // Grains.GetGrain<ICounterEntity>(EntityId.For(firstOwner, name)) call reach the wall.
        // GetEntity always scopes the grain id to ITS OWN Owner, so it silently addresses a
        // distinct, empty entity instead of crossing owners. The raw GetGrain call DOES target
        // the first owner's real grain id, but OwnerBoundCallFilter's owner comparison only
        // runs for an ATTRIBUTED caller (itself a grain with an "{owner}/{name}" SourceId); an
        // external test client is never attributed, so it hit ICounterEntity's
        // [ClientEntryPoint] grant and succeeded instead of throwing. Only a grain-to-grain
        // call is genuinely attributed, so the stranger's OWN entity is asked to reach across
        // to the first owner's entity on the external caller's behalf.
        var strangerBrain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("stranger"));
        var stranger = strangerBrain.GetEntity<ICounterEntity>(fixture.Sim.UniqueId("probe"));
        var target = EntityId.For<ICounterEntity>(firstOwner, name);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => stranger.ReachAcrossOwner(target));
    }

    [Fact]
    public void BareMarkerEntityContractIsRefused()
        => Assert.Throws<NeuronAuthorizationException>(() => fixture.Sim.Brain.GetEntity<IEntity>());
}
