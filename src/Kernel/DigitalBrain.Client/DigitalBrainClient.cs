using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Brain;
namespace DigitalBrain.Client;

public sealed class DigitalBrainClient : IDigitalBrain
{
    private static readonly TimeSpan ResponsePollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IGrainFactory _grains;

    private DigitalBrainClient(IGrainFactory grains, OwnerId owner)
    {
        _grains = grains;
        Owner = owner;
    }

    public OwnerId Owner { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DigitalBrainClient Connect(IGrainFactory grains, string owner)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return new DigitalBrainClient(grains, new OwnerId(owner));
    }

    public Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Session().Activate();
    }

    public NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        return new NeuronReference<TNeuron>(this, name);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TNeuron GetGrainProxy<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));

        return _grains.GetGrain<TNeuron>(NeuronId.For<TNeuron>(Owner, name).ToGrainId());
    }

    public TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainEntityContract(typeof(TEntity));

        return _grains.GetGrain<TEntity>(EntityId.For<TEntity>(Owner, name).ToGrainId());
    }

    public Task FireAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken = default)
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return Get<TNeuron>(name).FireAsync(synapse, cancellationToken);
    }

    public Task<JournalRead> ReadJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        RequireOwnedSubject(subject);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        cancellationToken.ThrowIfCancellationRequested();
        return Session().ReadNeuronJournal(subject, kind, afterSequence);
    }

    public async IAsyncEnumerable<JournalRead> WatchJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequireOwnedSubject(subject);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCreateJournalObserver(kind, out var observer, out var reference))
        {

            var cursor = afterSequence;
            while (!cancellationToken.IsCancellationRequested)
            {
                var page = await Session().ReadNeuronJournal(subject, kind, cursor).ConfigureAwait(false);
                if (page.Delta.Count > 0 || page.ResetSnapshot is not null)
                {
                    yield return page;
                }

                cursor = page.ResumeSequence;
                await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
            }

            yield break;
        }

        var session = Session();
        try
        {
            await session.WatchNeuron(subject, kind, afterSequence, reference).ConfigureAwait(false);
            await foreach (var page in observer.Reads.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return page;
            }
        }
        finally
        {
            await TeardownWatchAsync(session, subject, reference, observer).ConfigureAwait(false);
        }
    }

    internal Task SendToAsync(NeuronId receiver, Synapse synapse, CancellationToken cancellationToken)
        => SendValidatedAsync(receiver, synapse, cancellationToken);

    internal async Task<TResponse> SendRequestAsync<TResponse>(
        NeuronId receiver,
        Synapse request,
        CancellationToken cancellationToken)
        where TResponse : Synapse
    {
        var response = await SendRequestAsync(receiver, request, typeof(TResponse), cancellationToken)
            .ConfigureAwait(false);
        return (TResponse)response;
    }

    public async Task<Synapse> SendRequestAsync(
        NeuronId receiver,
        Synapse request,
        Type responseType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseType);
        if (!typeof(Synapse).IsAssignableFrom(responseType) || responseType.IsAbstract || responseType.IsInterface)
        {
            throw new ArgumentException(
                $"Response type '{responseType}' must be a concrete Synapse.",
                nameof(responseType));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var sessionId = ISessionNeuron.ForOwner(Owner);
        var session = Session();
        // long.MaxValue: only the resume cursor — do not deserialize the whole journal history
        // (polymorphic Synapse entries can fail client-side if any fact type is missing).
        var cursor = await session
            .ReadNeuronJournal(sessionId, JournalKind.Incoming, afterSequence: long.MaxValue)
            .ConfigureAwait(false);

        if (!TryCreateJournalObserver(JournalKind.Incoming, out var observer, out var reference))
        {
            var polled = await SendValidatedAsync(receiver, request, cancellationToken).ConfigureAwait(false);
            return await PollForResponseAsync(
                session,
                sessionId,
                cursor.ResumeSequence,
                polled.CorrelationId,
                responseType,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await session.WatchNeuron(sessionId, JournalKind.Incoming, cursor.ResumeSequence, reference).ConfigureAwait(false);

            var delivery = await SendValidatedAsync(receiver, request, cancellationToken).ConfigureAwait(false);
            return await WaitForResponseAsync(
                observer,
                delivery.CorrelationId,
                responseType,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await TeardownWatchAsync(session, sessionId, reference, observer).ConfigureAwait(false);
        }
    }

    private ISessionNeuron Session()
        => _grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(Owner).ToGrainId());

    private void RequireOwnedSubject(NeuronId subject)
    {
        if (subject.Owner != Owner)
        {
            throw new NeuronAuthorizationException(
                $"Client owner '{Owner}' cannot observe journal of neuron '{subject}' owned by '{subject.Owner}'.");
        }
    }

    private async Task<SynapseDelivery> SendValidatedAsync(
        NeuronId receiver,
        Synapse synapse,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ActivateAsync(cancellationToken).ConfigureAwait(false);
        return await Session().Fire(receiver, synapse).ConfigureAwait(false);
    }

    private bool TryCreateJournalObserver(
        JournalKind kind,
        [NotNullWhen(true)] out ChannelJournalObserver? observer,
        [NotNullWhen(true)] out IJournalObserver? reference)
    {
        var candidate = new ChannelJournalObserver(kind);
        try
        {
            reference = _grains.CreateObjectReference<IJournalObserver>(candidate);
            observer = candidate;
            return true;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("object reference", StringComparison.OrdinalIgnoreCase))
        {
            candidate.Complete();
            observer = null;
            reference = null;
            return false;
        }
    }

    // An unrouted emission never produces the awaited reply, so without this the caller
    // waits out its own token on a request nothing was connected to receive. (Settled
    // refusals throw out of Session().Fire directly.)
    private static void RequireNoRefusal(Synapse candidate, CorrelationId correlation)
    {
        if (candidate is Unrouted unrouted && unrouted.Correlation == correlation)
        {
            throw new NeuronAuthorizationException(
                $"Nothing is connected to receive '{unrouted.Alias}' from {unrouted.Source}.");
        }
    }

    private static async Task<Synapse> WaitForResponseAsync(
        ChannelJournalObserver observer,
        CorrelationId correlation,
        Type responseType,
        CancellationToken cancellationToken)
    {
        await foreach (var page in observer.Reads.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var delivery in page.Delta)
            {
                RequireNoRefusal(delivery.Synapse, correlation);

                if (delivery.CorrelationId == correlation
                    && responseType.IsInstanceOfType(delivery.Synapse))
                {
                    return delivery.Synapse;
                }
            }
        }

        throw new InvalidOperationException(
            $"The session journal watch ended before a '{responseType.Name}' response arrived for correlation '{correlation}'.");
    }

    private static async Task<Synapse> PollForResponseAsync(
        ISessionNeuron session,
        NeuronId sessionId,
        long cursor,
        CorrelationId correlation,
        Type responseType,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var read = await session.ReadNeuronJournal(sessionId, JournalKind.Incoming, cursor).ConfigureAwait(false);
            foreach (var candidate in read.Delta)
            {
                RequireNoRefusal(candidate.Synapse, correlation);

                if (candidate.CorrelationId == correlation
                    && responseType.IsInstanceOfType(candidate.Synapse))
                {
                    return candidate.Synapse;
                }
            }

            if (read.ResetSnapshot is not null)
            {
                throw new InvalidOperationException(
                    $"The session journal compacted past sequence {cursor} before a "
                    + $"'{responseType.Name}' response arrived for correlation '{correlation}'.");
            }

            cursor = read.ResumeSequence;
            await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
    private async Task TeardownWatchAsync(
        ISessionNeuron session,
        NeuronId subject,
        IJournalObserver reference,
        ChannelJournalObserver observer)
    {
        try
        {
            await session.UnwatchNeuron(subject, reference).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            _grains.DeleteObjectReference<IJournalObserver>(reference);
        }
        catch (Exception)
        {
        }
        finally
        {
            observer.Complete();
        }
    }

    private static void RequireDomainNeuronContract(Type neuronType)
    {
        if (neuronType == typeof(INeuron)
            || typeof(ISessionNeuron).IsAssignableFrom(neuronType))
        {
            throw new NeuronAuthorizationException(
                $"'{neuronType.Name}' is not addressable through IDigitalBrain.Get. Activate the brain with ActivateAsync; address domain neuron contracts with Get; fire synapses through FireAsync; observe journals through ReadJournalAsync and WatchJournalAsync.");
        }
    }

    private static void RequireDomainEntityContract(Type entityType)
    {
        if (entityType == typeof(IEntity))
        {
            throw new NeuronAuthorizationException(
                $"'{entityType.Name}' is not addressable through IDigitalBrain.GetEntity. Address a concrete entity contract with GetEntity.");
        }
    }
}
