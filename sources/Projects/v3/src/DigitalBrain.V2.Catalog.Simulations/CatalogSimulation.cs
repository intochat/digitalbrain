using DigitalBrain.V2.Core.Synapses;
using DigitalBrain.V2.Testing;
using Greeter.Contracts;
using Ping.Contracts;
using Xunit;

namespace DigitalBrain.V2.Catalog.Simulations;

public sealed class CatalogSimulation : Simulation
{
    protected override Task PrimeAsync() => Activate<ICatalogNeuron>();

    [Fact]
    public async Task Catalog_describes_the_constellation_edges()
    {
        await Fire(new DescribeConstellation(
        [
            typeof(IPingNeuron).Assembly.GetName().Name!,
            typeof(IGreeterNeuron).Assembly.GetName().Name!
        ])
        { Routing = RoutingMode.Broadcast });

        var described = await Expect<ConstellationDescribed>();
        Console.WriteLine(described.Catalog.ToConstellationText());

        var ping = Edge(described.Catalog, "Ping", "Pong");
        var greeter = Edge(described.Catalog, "Greeter", "Announce");

        Assert.Equal("*", ping.To);
        Assert.Equal("Room", greeter.To);
    }

    private static CatalogEdge Edge(CatalogDocument catalog, string from, string synapse)
    {
        return catalog.Edges.FirstOrDefault(edge =>
            edge.From == from && edge.Synapse == synapse)
            ?? throw new InvalidOperationException($"Missing edge {from} --{synapse}--> ?");
    }
}
