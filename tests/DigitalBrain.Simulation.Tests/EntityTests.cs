using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.UI;
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
    public void BareMarkerEntityContractIsRefused()
        => Assert.Throws<NeuronAuthorizationException>(() => fixture.Sim.Brain.GetEntity<IEntity>());

    [Fact]
    public async Task ChartEntityRendersAndReadsItsState()
    {
        var chart = fixture.Sim.Brain.GetEntity<IChart>(fixture.Sim.UniqueId("chart"));
        var state = new ChartState("Sales", "bar", [new ChartPoint("Q1", 10), new ChartPoint("Q2", 20)]);

        await chart.Render(state);
        var read = await chart.Read();

        Assert.NotNull(read);
        Assert.Equal("Sales", read.Title);
        Assert.Equal(2, read.Points.Count);
    }

    [Fact]
    public async Task ImageEntityDescribesAndReadsItsState()
    {
        var image = fixture.Sim.Brain.GetEntity<IImage>(fixture.Sim.UniqueId("image"));
        var state = new ImageState("a red fox", "gpt-image-1", "image/png", "test-image-1.png");

        await image.Describe(state);
        var read = await image.Read();

        Assert.NotNull(read);
        Assert.Equal("a red fox", read.Prompt);
    }

    [Fact]
    public async Task MemoryImageStoreRoundTripsBytes()
    {
        var store = new MemoryKitImageStore();
        await store.SaveAsync("x.png", new byte[] { 1, 2, 3 }, "image/png", CancellationToken.None);

        var read = await store.ReadAsync("x.png", CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(new byte[] { 1, 2, 3 }, read!.Value.Content);
    }
}
