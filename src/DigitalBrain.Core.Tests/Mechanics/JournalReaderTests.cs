using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class JournalReaderTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>();

    [Fact]
    public async Task ReturnsOrderedRecordedPagesWithAnExactContinuation()
    {
        const string name = "reader-pages";
        var source = new NeuronId("digitalbrain.synapse-source", name);

        await PublishAsync(name, new MechanicsStart(), Cancellation);
        await PublishAsync(name, new MechanicsStart(Echo: true), Cancellation);

        var first = await ReadAsync(source, afterPosition: 0, maximumRecords: 1, Cancellation);

        var firstRecord = Assert.Single(first.Records);
        Assert.Equal(1, firstRecord.Position);
        Assert.Equal(1, first.ReadThroughPosition);
        Assert.Equal(2, first.JournalEndPosition);
        Assert.Equal(typeof(MechanicsStart).FullName, firstRecord.SynapseKind);
        Assert.False(firstRecord.Serialization.GetProperty("echo").GetBoolean());

        var second = await ReadAsync(source, first.ReadThroughPosition, maximumRecords: 1, Cancellation);

        var secondRecord = Assert.Single(second.Records);
        Assert.Equal(2, secondRecord.Position);
        Assert.Equal(2, second.ReadThroughPosition);
        Assert.Equal(2, second.JournalEndPosition);
        Assert.True(secondRecord.Serialization.GetProperty("echo").GetBoolean());
    }

    [Fact]
    public async Task ReportsUnavailableHistoryWhenACursorPrecedesTheJournalRange()
    {
        const string name = "reader-history-range";
        var source = new NeuronId("digitalbrain.synapse-source", name);

        await PublishAsync(name, new MechanicsStart(), Cancellation);

        var read = await ReadOutcomeAsync(source, afterPosition: -1, maximumRecords: 1, Cancellation);

        var unavailable = Assert.IsType<JournalHistoryUnavailable>(read);
        Assert.Equal(-1, unavailable.RequestedAfterPosition);
        Assert.Equal(1, unavailable.AvailableFromPosition);
        Assert.Equal(1, unavailable.JournalEndPosition);
    }
}
