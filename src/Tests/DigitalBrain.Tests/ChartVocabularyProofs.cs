using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ChartVocabularyProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task EmittedChartPointLandsOnItsBoundChart()
    {
        var brain = fixture.BrainFor("chart");
        var session = ISessionNeuron.ForOwner(brain.Owner);
        var chart = NeuronId.For<IChart>(brain.Owner, "dashboard");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), session, ChartPoint.AliasName, chart),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, session, ChartPoint.AliasName);

        await brain.FireAsync(new ChartPoint("cpu", "12:00", 42), TestContext.Current.CancellationToken);

        var charted = await Journals.WaitForAsync(
            brain, chart, JournalKind.Incoming,
            delivery => delivery.Synapse is ChartPoint { Series: "cpu", Label: "12:00", Value: 42 });
        Assert.Equal(session, charted.Caller);

        var points = await brain.GetGrainProxy<IChart>("dashboard").Read();
        Assert.Contains(points, point => point is { Series: "cpu", Label: "12:00", Value: 42 });
    }
}
