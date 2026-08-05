namespace DigitalBrain;

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
        private protected override bool ContinuesAsks => false;

        async Task ISessionEntry.EmitAsync(Synapse fact)
        {
            ArgumentNullException.ThrowIfNull(fact);
            RefusePoisoned();
            var staged = StagedFor(fact);

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
            var staged = StagedFor(fact);

            try
            {
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
            var staged = StagedFor(question);
            staged = catalog.TryGetAnswererKind(questionType, out var answererKind)
                ? staged with { AskAnswererKind = answererKind }
                : staged with { AskLacksAnswerer = true };

            long position;
            bool deliverable;
            try
            {
                position = journal.LastSeq + 1;
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

    public async Task<TReply> AskAsync<TReply>(Synapse question, CancellationToken cancellationToken = default)
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
                    case TReply reply when fact.Answers == askRef:
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

public sealed class AskOutcomeUnknownException : Exception
{
    internal AskOutcomeUnknownException(NeuronId session, Exception wireFailure)
        : base($"The ask wire call on session {session} failed before an outcome was observed; "
            + "read the session journal to learn whether the ask committed — never refire "
            + "(a second fire would journal a second ask).", wireFailure)
        => Session = session;

    public NeuronId Session { get; }
}
