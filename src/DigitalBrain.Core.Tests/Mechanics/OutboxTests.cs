using DigitalBrain.Testing;

namespace DigitalBrain;

public sealed class OutboxTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<OutboxEmitter>().AddModule<OutboxReceiver>();

    [Fact]
    public async Task CommitsTheSaidReceiverSnapshotBeforeTheReceiverHearsTheFact()
    {
        const string name = "mechanics";
        var session = Brain.Session(name);
        var emitter = new NeuronId("outboxemitter", name);
        var receiver = new NeuronId("outboxreceiver", name);

        await session.EmitAsync(new OutboxStart(), Cancellation);

        var senderJournal = await WaitForJournalAsync(
            emitter,
            reading => reading.Journal.Any(fact => fact.Entry == "said" && fact.Body is OutboxPulse),
            "a committed outbox pulse",
            Cancellation);
        var said = senderJournal.Journal.Single(fact => fact.Entry == "said" && fact.Body is OutboxPulse);
        Assert.Contains(receiver, said.To ?? []);

        var receiverJournal = await WaitForJournalAsync(
            receiver,
            reading => reading.Journal.Any(fact => fact.Entry == "heard" && fact.Body is OutboxPulse),
            "a delivered outbox pulse",
            Cancellation);
        var heard = receiverJournal.Journal.Single(fact => fact.Entry == "heard" && fact.Body is OutboxPulse);
        Assert.Equal(emitter, heard.Metadata.Source);
        Assert.Equal(said.Position, heard.Metadata.Sequence);
    }

    [Fact]
    public async Task ReturnsAfterCommitInsteadOfWaitingForAChildOutbox()
    {
        const string name = "cycle";
        var emitter = new NeuronId("outboxemitter", name);

        await Brain.Session(name)
            .EmitAsync(new OutboxStart(Echo: true), Cancellation)
            .WaitAsync(TimeSpan.FromSeconds(2), Cancellation);

        _ = await WaitForJournalAsync(
            emitter,
            reading => reading.Journal.Any(fact => fact.Entry == "heard" && fact.Body is OutboxEcho),
            "an asynchronously delivered outbox echo",
            Cancellation);
    }

    [Fact]
    public async Task JournalsSpeechWithNoDeclaredReceiver()
    {
        const string name = "audit";
        var emitter = new NeuronId("outboxemitter", name);

        await Brain.Session(name).EmitAsync(new OutboxStart(Audit: true), Cancellation);

        var reading = await WaitForJournalAsync(
            emitter,
            journal => journal.Journal.Any(fact => fact.Entry == "said" && fact.Body is OutboxAudit),
            "a committed zero-receiver speech entry",
            Cancellation);
        var said = reading.Journal.Single(fact => fact.Entry == "said" && fact.Body is OutboxAudit);
        Assert.Empty(said.To ?? []);
    }
}

public sealed record OutboxStart(bool Echo = false, bool Audit = false) : Synapse;

public sealed record OutboxPulse(bool Echo) : Synapse;

public sealed record OutboxEcho : Synapse;

public sealed record OutboxAudit : Synapse;

[GrainType("outboxemitter")]
public sealed class OutboxEmitter : Neuron, INeuron<OutboxStart>, INeuron<OutboxEcho>
{
    public Task HandleAsync(OutboxStart synapse, CancellationToken cancellationToken)
    {
        Emit(new OutboxPulse(synapse.Echo));
        if (synapse.Audit)
        {
            Emit(new OutboxAudit());
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(OutboxEcho synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

[GrainType("outboxreceiver")]
public sealed class OutboxReceiver : Neuron, INeuron<OutboxPulse>
{
    public Task HandleAsync(OutboxPulse synapse, CancellationToken cancellationToken)
    {
        if (synapse.Echo)
        {
            Emit(new OutboxEcho());
        }

        return Task.CompletedTask;
    }
}
