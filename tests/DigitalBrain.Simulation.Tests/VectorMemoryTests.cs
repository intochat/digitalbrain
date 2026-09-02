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
        var payload = new ProtectedPayloadReference(
            Guid.Parse("d7dd4eec-dadb-46f5-9cb9-1f1cd44c9b55"),
            new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var cancellationToken = TestContext.Current.CancellationToken;

        var stored = await memory.SendAsync(
            new StoreVectorMemory(
                memoryNamespace,
                "meeting",
                "Discuss the calendar event",
                Metadata: null,
                Payload: payload),
            cancellationToken);

        Assert.True(stored.Stored);
        Assert.Equal(VectorMemoryStoreStatus.Stored, stored.Status);

        var matches = await memory.SendAsync(
            new SearchVectorMemory(memoryNamespace, "calendar", Limit: 1, Metadata: null),
            cancellationToken);

        var match = Assert.Single(matches.Matches);
        Assert.Equal("meeting", match.Key);
        Assert.Equal("Discuss the calendar event", match.Text);
        Assert.Equal(payload, match.Payload);
    }
}
