using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class HarnessSmoke(BrainClusterFixture fixture)
{
    [Fact]
    public async Task PokedSourceCommitsProbeFactIntoItsOutgoingJournal()
    {
        var brain = fixture.BrainFor("smoke");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "s1");

        await brain.FireAsync<IProbeSource>("s1", new Poke("hello"), TestContext.Current.CancellationToken);

        var emitted = await Journals.WaitForAsync(
            brain, source, JournalKind.Outgoing, delivery => delivery.Synapse is ProbeFact);
        Assert.Equal("hello", ((ProbeFact)emitted.Synapse).Text);
    }
}
