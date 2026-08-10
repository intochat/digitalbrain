using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class DeclarativeTransformProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task DataAuthoredMappingAdaptsWithoutAnyRegisteredTransform()
    {
        var brain = fixture.BrainFor("declarative");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "feed");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), source, "probe.fact", sink, "to:ui.item-appended{Title=Text}"),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, source, "probe.fact");

        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("declared at runtime"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, sink, JournalKind.Incoming,
            delivery => delivery.Synapse is ItemAppended { Title: "declared at runtime" });
    }
}
