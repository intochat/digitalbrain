namespace DigitalBrain;

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
