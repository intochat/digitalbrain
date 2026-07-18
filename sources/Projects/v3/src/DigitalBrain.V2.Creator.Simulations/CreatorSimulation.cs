using DigitalBrain.V2.Catalog;
using DigitalBrain.V2.Core.Synapses;
using DigitalBrain.V2.Creator;
using DigitalBrain.V2.Testing;
using Xunit;

namespace DigitalBrain.V2.Creator.Simulations;

public sealed class CreatorSimulation : Simulation
{
    protected override async Task PrimeAsync()
    {
        await Activate<IArchitectNeuron>();
        await Activate<ICatalogNeuron>();
        await Activate<IImplementerNeuron>();
        await Activate<IGateNeuron>();
    }

    [Fact]
    public async Task Creator_loop_authors_gates_and_activates_a_neuron()
    {
        await Fire(new CreateNeuron("Generated.PingEcho") { Routing = RoutingMode.Broadcast });

        var activated = await Expect<NeuronActivated>(
            synapse => synapse.Capability == "Generated.PingEcho",
            ms: 15000);

        Assert.Equal("Generated.PingEcho", activated.Capability);
    }
}
