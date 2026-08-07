using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class RecordedTurnRecoveryTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>();

    [Fact]
    public async Task ReloadsFromRecordedTruthAfterARecordingFailure()
    {
        const string name = "recording-reload";
        var source = new NeuronId("digitalbrain.synapse-source", name);
        var fault = FailNextJournalRecording(source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PublishAsync(name, new MechanicsStart(), Cancellation));
        await fault.Consumed.WaitAsync(Cancellation);
        await DeactivateAsync([source], Cancellation);

        await PublishAsync(name, new MechanicsStart(Echo: true), Cancellation);

        var page = await ReadAsync(source, cancellationToken: Cancellation);
        var record = Assert.Single(page.Records);
        Assert.Equal(1, record.Position);
        Assert.True(record.Serialization.GetProperty("echo").GetBoolean());
    }
}
