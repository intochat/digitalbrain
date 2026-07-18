using DigitalBrain.V2.Core.Synapses;
using DigitalBrain.V2.Testing;
using Greeter.Contracts;
using Xunit;

namespace Greeter.Simulations;

public sealed class GreeterSimulation : Simulation
{
    protected override async Task PrimeAsync()
    {
        await Activate<IGreeterNeuron>();
        await Activate<IRoomNeuron>();
        await Activate<IBystanderNeuron>();
    }

    [Fact]
    public async Task Hello_is_announced_through_point_to_point_ask()
    {
        await Fire(new Hello("alice") { Routing = RoutingMode.Broadcast });

        var announced = await Expect<Announced>(a => a.Name == "alice");
        var bystander = await Expect<BystanderHeardHello>(b => b.Name == "alice");

        Assert.Equal("alice", announced.Name);
        Assert.Equal(RoutingMode.Broadcast, announced.Routing);
        Assert.Equal("alice", bystander.Name);
        Assert.NotEqual(Guid.Empty, announced.CorrelationId);
        Assert.Equal(announced.CorrelationId, bystander.CorrelationId);
        await ExpectNone<Announce>();
        await ExpectNone<BystanderHeardAnnounced>();
    }
}
