using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class TurnStateTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(CounterInput).Assembly)
            .RegisterNeuron<CounterBehavior>("counter");

    [Fact]
    public async Task UsesAFreshBehaviorPerInputWhileRecordingTouchedStateAcrossTurns()
    {
        const string name = "stateful";
        var counter = new NeuronId("counter", name);

        await PublishAsync(name, new CounterInput(), Cancellation);
        await PublishAsync(name, new CounterInput(), Cancellation);

        var page = await WaitForJournalAsync(
            counter,
            observed => observed.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(CounterReported).FullName) == 2,
            "two recorded counter reports",
            Cancellation);
        var reports = page.Records
            .Where(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(CounterReported).FullName)
            .OrderBy(record => record.Position)
            .ToArray();

        Assert.Equal(1, reports[0].Serialization.GetProperty("value").GetInt32());
        Assert.Equal(2, reports[1].Serialization.GetProperty("value").GetInt32());
        Assert.NotEqual(
            reports[0].Serialization.GetProperty("instance").GetInt32(),
            reports[1].Serialization.GetProperty("instance").GetInt32());
    }
}
