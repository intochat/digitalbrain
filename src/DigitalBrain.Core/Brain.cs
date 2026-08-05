namespace DigitalBrain;

// The session grain and its wire (§5, the edge). The class is nested so its name mints the
// Core-owned "session" kind through the one convention (NeuronId.KindOf) and so it reaches
// the turn machinery it shares with every neuron — journal, watermark, outbox, connections.
// Each entry runs ONE session turn and returns after the commit; delivery rides the same
// drain as any neuron's. Edge-born facts carry Cause: null.
public abstract partial class Neuron
{
    [Alias("db.session")]
    internal interface ISessionEntry : IGrainWithStringKey
    {
        [Alias("emit")]
        Task EmitAsync(Synapse fact);

        [Alias("send")]
        Task SendAsync(NeuronId receiver, Synapse fact);

        [Alias("ask")]
        Task<SynapseRef> AskAsync(Synapse question);
    }

    internal sealed class Session : Neuron, ISessionEntry
    {
        // The session declares no continuations; its asks close by the journaled reception
        // alone (ReceiveReplyAsync releases the pin, the edge poll matches on Answers).
        private protected override bool ContinuesAsks => false;

        async Task ISessionEntry.EmitAsync(Synapse fact)
        {
            ArgumentNullException.ThrowIfNull(fact);
            RefusePoisoned();
            RefuseContinuationEmission(fact.GetType());
            var staged = StagedFor(fact);   // unknown vocabulary refuses here, before staging

            bool deliverable;
            try
            {
                deliverable = StageSaid(
                    staged, cause: null, clock.GetUtcNow(), replyTo: null, journal.OpenAsksSnapshot());
            }
            catch
            {
                Poison();
                throw;
            }

            await CommitCoreBatchAsync(deliverable);
        }

        async Task ISessionEntry.SendAsync(NeuronId receiver, Synapse fact)
        {
            ArgumentNullException.ThrowIfNull(fact);
            RefusePoisoned();
            RefuseContinuationEmission(fact.GetType());
            var staged = StagedFor(fact);

            try
            {
                // Directed: exactly the named receiver, no declaration or connection
                // fan-out — the route a Session.SendAsync names is the route it gets.
                journal.AppendSaid(
                    staged.Kind,
                    clock.GetUtcNow(),
                    cause: null,
                    answers: null,
                    to: [NeuronIdEntry.From(receiver, NeuronIdEntry.Ask)],
                    staged.Body);
            }
            catch
            {
                Poison();
                throw;
            }

            await CommitCoreBatchAsync(deliverable: true);
        }

        async Task<SynapseRef> ISessionEntry.AskAsync(Synapse question)
        {
            ArgumentNullException.ThrowIfNull(question);
            RefusePoisoned();
            var questionType = question.GetType();
            _ = Catalog.ReplyTypeOf(questionType);   // only questions can be asked — refuse loudly
            var staged = StagedFor(question);
            staged = catalog.TryGetAnswererKind(questionType, out var answererKind)
                ? staged with { AskAnswererKind = answererKind }
                : staged with { AskLacksAnswerer = true };

            long position;
            bool deliverable;
            try
            {
                position = journal.LastSeq + 1;   // the question's said entry stages first
                deliverable = StageSaid(
                    staged, cause: null, clock.GetUtcNow(), replyTo: null, journal.OpenAsksSnapshot());
            }
            catch
            {
                Poison();
                throw;
            }

            await CommitCoreBatchAsync(deliverable);
            return new SynapseRef(Id, position);
        }
    }
}

// The edge (§5): sessions and reads over the one grain factory a cluster client hands out.
// Get<TNeuron> died as a send/ask surface — the edge speaks facts and reads journals.
public sealed class Brain(IGrainFactory grains)
{
    public Session Session(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        return new Session(grains, context);
    }

    public async Task<NeuronReading> ReadAsync(
        NeuronId neuron, long afterPosition = 0, CancellationToken cancellationToken = default)
        => await grains
            .GetGrain<Neuron.ITransport>(Neuron.AddressOf(neuron))
            .ReadAsync(afterPosition)
            .WaitAsync(cancellationToken);
}

// The client half of a session: every wire call fires EXACTLY once and returns after the
// session turn commits. The Task is volatile sugar — the journal is the ask: a crashed and
// restarted edge reconstructs the whole round trip from the session journal alone.
public sealed class Session
{
    private static readonly TimeSpan PollBackoff = TimeSpan.FromMilliseconds(75);

    private readonly IGrainFactory grains;

    internal Session(IGrainFactory grains, string context)
    {
        this.grains = grains;
        Id = new NeuronId(NeuronId.KindOf(typeof(Neuron.Session)), context);
    }

    public NeuronId Id { get; }

    public Task EmitAsync(Synapse fact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        cancellationToken.ThrowIfCancellationRequested();
        return Entry().EmitAsync(fact);
    }

    public Task SendAsync(NeuronId receiver, Synapse fact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        cancellationToken.ThrowIfCancellationRequested();
        return Entry().SendAsync(receiver, fact);
    }

    // Fire once, then poll the session journal from the ask's own position: the reception
    // stamped with Answers is the reply; DeliveryFailed or AskExpired for the ask is the
    // failure; a wire failure on the single fire is an ambiguous outcome — recovery is
    // reading the session journal, never retrying the call (a second fire would journal a
    // second ask that dedup correctly cannot catch).
    public async Task<TReply> AskAsync<TReply>(Synapse<TReply> question, CancellationToken cancellationToken = default)
        where TReply : Synapse
    {
        ArgumentNullException.ThrowIfNull(question);
        cancellationToken.ThrowIfCancellationRequested();

        SynapseRef askRef;
        try
        {
            askRef = await Entry().AskAsync(question);
        }
        catch (TimeoutException wireFailure)
        {
            throw new AskOutcomeUnknownException(Id, wireFailure);
        }
        catch (OrleansException wireFailure)
        {
            throw new AskOutcomeUnknownException(Id, wireFailure);
        }

        var transport = grains.GetGrain<Neuron.ITransport>(Neuron.AddressOf(Id));
        var cursor = askRef.Sequence;
        while (true)
        {
            RefuseCancelled(askRef, cancellationToken);
            var reading = await transport.ReadAsync(cursor);
            foreach (var fact in reading.Journal)
            {
                cursor = Math.Max(cursor, fact.Position);
                switch (fact.Body)
                {
                    case TReply reply when fact.Metadata.Answers == askRef:
                        return reply;
                    case DeliveryFailed failed when failed.Fact == askRef:
                        throw new AskFailedException(Id, askRef, failed);
                    case AskExpired expired when expired.Ask == askRef:
                        throw new AskFailedException(Id, askRef, expired);
                    default:
                        break;
                }
            }

            try
            {
                await Task.Delay(PollBackoff, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                RefuseCancelled(askRef, cancellationToken);
                throw;
            }
        }
    }

    private void RefuseCancelled(SynapseRef askRef, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"Polling for the answer to ask {askRef.Source}/{askRef.Sequence} on session {Id} "
                + "was cancelled; the session journal still holds — or will hold — its outcome.",
                cancellationToken);
        }
    }

    private Neuron.ISessionEntry Entry()
        => grains.GetGrain<Neuron.ISessionEntry>(Neuron.AddressOf(Id));
}

// The ask ended in a journaled Core fact instead of a reply: DeliveryFailed (the ask never
// landed, or no answerer exists) or AskExpired (delivered, never answered inside the
// horizon). The fact IS the failure record; the exception just carries it to the caller.
public sealed class AskFailedException : Exception
{
    internal AskFailedException(NeuronId session, SynapseRef ask, DeliveryFailed failure)
        : base($"Ask {ask.Source}/{ask.Sequence} on session {session} failed: delivery to "
            + $"{failure.Receiver} — {failure.Reason} (attempts: {failure.Attempts}).")
        => Fact = failure;

    internal AskFailedException(NeuronId session, SynapseRef ask, AskExpired expired)
        : base($"Ask {ask.Source}/{ask.Sequence} on session {session} expired: "
            + $"'{expired.Question}' was never answered inside the ask horizon.")
        => Fact = expired;

    public Synapse Fact { get; }
}

// The single fire's wire call failed, so whether the session turn committed is unknown.
// Recovery is reading the session journal — never retrying the fire.
public sealed class AskOutcomeUnknownException : Exception
{
    internal AskOutcomeUnknownException(NeuronId session, Exception wireFailure)
        : base($"The ask wire call on session {session} failed before an outcome was observed; "
            + "read the session journal to learn whether the ask committed — never refire "
            + "(a second fire would journal a second ask).", wireFailure)
        => Session = session;

    public NeuronId Session { get; }
}
