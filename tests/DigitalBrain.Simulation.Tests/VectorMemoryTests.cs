using DigitalBrain.Abstractions;
using DigitalBrain.Memory;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class VectorMemoryTests(SimulationFixture fixture)
{
    [Fact]
    public async Task ExplicitEmbeddingModelSupportsVectorMemoryStoreAndSearch()
    {
        var memory = fixture.Sim.BrainFor(fixture.Sim.UniqueId("vector-owner"))
            .Get<IVectorMemory>("notes");
        var memoryNamespace = new VectorMemoryNamespace("notes");
        var cancellationToken = TestContext.Current.CancellationToken;

        var stored = await memory.FireAsync(
            new StoreVectorMemory(
                memoryNamespace,
                "meeting",
                "Discuss the calendar event",
                Metadata: null,
                Payload: null),
            cancellationToken);

        Assert.True(stored.Stored);
        Assert.Equal(VectorMemoryStoreStatus.Stored, stored.Status);

        var matches = await memory.FireAsync(
            new SearchVectorMemory(memoryNamespace, "calendar", Limit: 1, Metadata: null),
            cancellationToken);

        var match = Assert.Single(matches.Matches);
        Assert.Equal("meeting", match.Key);
        Assert.Equal("Discuss the calendar event", match.Text);
    }
}
