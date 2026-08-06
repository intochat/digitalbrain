using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class PassiveJournalReaderTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(PassiveProbeSynapse).Assembly)
            .RegisterNeuron<PassiveProbeBehavior>("passive-probe");

    [Fact]
    public async Task ReadingAJournalDoesNotConstructOrRunBehavior()
    {
        PassiveProbeBehavior.Reset();
        var probe = new NeuronId("passive-probe", "read-only");

        var page = await ReadAsync(probe, cancellationToken: Cancellation);

        Assert.Empty(page.Records);
        Assert.Equal(0, PassiveProbeBehavior.Constructions);
    }
}
