using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Grants;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Client;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// Pins GrantsNeuron.RequireReadAccessAsync -- the gate read_chart (and the renderer's own write
// path) share, since a pure entity cannot open the capability turn IGrants.HasAccess needs.
// Ownership always passes; a bystander is denied until an explicit db.grant-access covers them.
[Collection(SimulationCollection.Name)]
public sealed class GrantsReadAccessTests(SimulationFixture fixture)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task OwnerReadsTheirOwnPartitionedSubjectWithoutAnyGrant()
    {
        var owner = new PrincipalId(Guid.NewGuid());
        var subject = new NeuronId(
            "chart",
            fixture.Sim.Brain.Owner,
            PrincipalPartition.InstanceName(owner, fixture.Sim.UniqueId("sales")));
        var cancellationToken = TestContext.Current.CancellationToken;

        using var _ = VerifiedActor.Enter(new ActorContext(owner, "owner"));

        // Ownership alone satisfies the read gate -- no exception thrown is the assertion.
        await GrantsNeuron.RequireReadAccessAsync(fixture.Sim.Grains, subject, cancellationToken);
    }

    [Fact]
    public async Task BystanderIsDeniedThenAllowedOnceGrantAccessCoversThem()
    {
        var owner = new PrincipalId(Guid.NewGuid());
        var bystander = new PrincipalId(Guid.NewGuid());
        var subject = new NeuronId(
            "chart",
            fixture.Sim.Brain.Owner,
            PrincipalPartition.InstanceName(owner, fixture.Sim.UniqueId("sales")));
        var cancellationToken = TestContext.Current.CancellationToken;

        using (VerifiedActor.Enter(new ActorContext(bystander, "bystander")))
        {
            var denied = await Assert.ThrowsAsync<NeuronAuthorizationException>(
                () => GrantsNeuron.RequireReadAccessAsync(fixture.Sim.Grains, subject, cancellationToken));
            Assert.Contains("denied", denied.Message, StringComparison.OrdinalIgnoreCase);
        }

        var grantsName = IGrants.ForPrincipal(fixture.Sim.Brain.Owner, owner).Name;
        using (VerifiedActor.Enter(new ActorContext(owner, "owner")))
        {
            await fixture.Sim.Brain
                .Get<IGrants>(grantsName)
                .FireAsync<AccessGranted>(
                    new GrantAccess(CommandId.New(), bystander, subject, GrantKind.Read, Intent: null),
                    cancellationToken)
                .WaitAsync(Bound, cancellationToken);
        }

        using (VerifiedActor.Enter(new ActorContext(bystander, "bystander")))
        {
            // The grant now covers the bystander -- no exception thrown is the assertion.
            await GrantsNeuron.RequireReadAccessAsync(fixture.Sim.Grains, subject, cancellationToken);
        }
    }
}
