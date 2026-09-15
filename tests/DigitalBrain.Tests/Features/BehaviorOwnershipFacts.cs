using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorOwnershipFacts
{
    [Fact]
    public async Task Claim_is_exclusive_idempotent_and_survives_a_cold_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        var id = NeuronId.Plain("owned-node").ToGrainId();
        await using (var brain = await BrainSteps.StartSimulationAsync(directory))
        {
            var owner = brain.Grains.GetGrain<INeuronOwnership>(id);
            await owner.Claim("first");
            await owner.Claim("first");
            await owner.Release("second");
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Claim("second"));
        }

        await using (var brain = await BrainSteps.StartSimulationAsync(directory))
        {
            var owner = brain.Grains.GetGrain<INeuronOwnership>(id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Claim("second"));
            await owner.Release("first");
            await owner.Release("first");
            await owner.Claim("second");
        }
    }

    [Fact]
    public async Task Shared_leases_survive_restart_and_deliver_once_until_the_last_release()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        var source = NeuronId.Plain("shared-source").ToGrainId();
        var target = NeuronId.Plain("shared-target");
        await using (var brain = await BrainSteps.StartSimulationAsync(directory))
        {
            var owner = brain.Grains.GetGrain<INeuronOwnership>(source);
            await owner.ConnectFor("first", target, "Note");
            await owner.ConnectFor("first", target, "Note");
            await owner.ConnectFor("second", target, "Note");
        }

        await using (var brain = await BrainSteps.StartSimulationAsync(directory))
        {
            var neuron = brain.Grains.GetGrain<INeuron>(source);
            var owner = brain.Grains.GetGrain<INeuronOwnership>(source);
            Assert.Single(await neuron.ReadSynapses());
            await neuron.Fire(Signal.Create("Note", "{}"), null, null, TestContext.Current.CancellationToken);
            var receiver = brain.Grains.GetGrain<INeuron>(target.ToGrainId());
            Assert.Single((await receiver.ReadJournal(JournalKind.Incoming, 0)).Delta);
            await neuron.Disconnect(target, "Note");
            await owner.DisconnectFor("missing", target, "Note");
            await owner.DisconnectFor("first", target, "Note");
            Assert.Single(await neuron.ReadSynapses());
            await owner.DisconnectFor("second", target, "Note");
            Assert.Empty(await neuron.ReadSynapses());
        }

        await using (var brain = await BrainSteps.StartSimulationAsync(directory))
        {
            Assert.Empty(await brain.Grains.GetGrain<INeuron>(source).ReadSynapses());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manual_connections_survive_behavior_removal_in_either_creation_order(bool manualFirst)
    {
        await using var brain = await BrainSteps.StartSimulationAsync();
        var id = NeuronId.Plain("manual-source").ToGrainId();
        var target = NeuronId.Plain("manual-target");
        var neuron = brain.Grains.GetGrain<INeuron>(id);
        var owner = brain.Grains.GetGrain<INeuronOwnership>(id);
        if (manualFirst)
        {
            // A physical edge without lease metadata is also the legacy storage format.
            await neuron.Connect(target, "Note");
        }

        await owner.ConnectFor("behavior", target, "Note");
        if (!manualFirst)
        {
            await neuron.Connect(target, "Note");
        }

        await owner.DisconnectFor("behavior", target, "Note");
        Assert.Single(await neuron.ReadSynapses());
        await neuron.Disconnect(target, "Note");
        Assert.Empty(await neuron.ReadSynapses());
    }

    [Fact]
    public async Task Directed_fire_claims_manual_ownership_of_an_existing_behavior_connection()
    {
        await using var brain = await BrainSteps.StartSimulationAsync();
        var id = NeuronId.Plain("firing-source").ToGrainId();
        var target = NeuronId.Plain("firing-target");
        var neuron = brain.Grains.GetGrain<INeuron>(id);
        var owner = brain.Grains.GetGrain<INeuronOwnership>(id);
        await owner.ConnectFor("behavior", target, "Note");
        await neuron.Fire(Signal.Create("Note", "{}"), target, null, TestContext.Current.CancellationToken);
        await owner.DisconnectFor("behavior", target, "Note");
        Assert.Single(await neuron.ReadSynapses());
    }

    [Fact]
    public async Task Ownership_capacity_rejects_before_mutation_and_reuses_released_slots()
    {
        await using var brain = await BrainSteps.StartSimulationAsync();
        var id = NeuronId.Plain("bounded-source").ToGrainId();
        var target = NeuronId.Plain("bounded-target");
        var neuron = brain.Grains.GetGrain<INeuron>(id);
        var owner = brain.Grains.GetGrain<INeuronOwnership>(id);
        for (var index = 0; index < 64; index++)
        {
            await owner.ConnectFor($"owner-{index}", target, "Note");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ConnectFor("overflow", target, "Note"));
        await Assert.ThrowsAsync<ArgumentException>(() => owner.Claim(new string('x', 257)));
        await neuron.Connect(target, "Note");
        await owner.DisconnectFor("owner-0", target, "Note");
        await owner.ConnectFor("replacement", target, "Note");
        Assert.Single(await neuron.ReadSynapses());
    }
}
