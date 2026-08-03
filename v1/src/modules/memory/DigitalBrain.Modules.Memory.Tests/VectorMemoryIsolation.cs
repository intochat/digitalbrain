using DigitalBrain.Memory;
using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class VectorMemoryIsolation(MemoryFixture fixture)
{
    [Fact(DisplayName = "Owners cannot read or remove another owner's entries")]
    public async Task Owner_isolation_is_enforced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var alice = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var other = test.Owner(MemoryFixture.OtherOwner);
        var bob = other.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");

        await alice.SendAsync(
            new StoreVectorMemory(notes, "secret", "alice only payload", new Dictionary<string, string>(), null),
            cancellationToken);

        var bobSearch = await bob.SendAsync(
            new SearchVectorMemory(notes, "alice only payload", Limit: 5, Metadata: null),
            cancellationToken);
        Assert.Empty(bobSearch.Matches);

        var bobRemove = await bob.SendAsync(new RemoveVectorMemory(notes, "secret"), cancellationToken);
        Assert.False(bobRemove.Removed);

        var aliceSearch = await alice.SendAsync(
            new SearchVectorMemory(notes, "alice only payload", Limit: 5, Metadata: null),
            cancellationToken);
        Assert.Single(aliceSearch.Matches);
    }
}
