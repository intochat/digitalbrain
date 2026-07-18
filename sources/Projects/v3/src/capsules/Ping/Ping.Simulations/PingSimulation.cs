using DigitalBrain.V2.Core.Synapses;
using DigitalBrain.V2.Testing;
using Ping.Contracts;
using Xunit;

namespace Ping.Simulations;

// A test IS a simulation. No mocks, no fakes: fire a real synapse into a real silo and assert
// on what the substrate broadcasts back.
public sealed class PingSimulation : Simulation
{
    // Activate the echo neuron so it is subscribed to the timeline before we broadcast at it.
    protected override Task PrimeAsync() => Activate<IPingNeuron>();

    [Fact]
    public async Task Ping_is_echoed_as_a_broadcast_pong()
    {
        await Fire(new Contracts.Ping(From: "alice") { Routing = RoutingMode.Broadcast });

        var pong = await Expect<Pong>(p => p.To == "alice");

        Assert.Equal("alice", pong.To);
        Assert.Equal(RoutingMode.Broadcast, pong.Routing);
    }
}
