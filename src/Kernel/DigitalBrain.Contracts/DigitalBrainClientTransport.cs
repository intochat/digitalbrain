using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

internal sealed class DigitalBrainClientTransport
{
    private static readonly TimeSpan ResponsePollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IGrainFactory _grains;

    internal DigitalBrainClientTransport(IGrainFactory grains, OwnerId owner)
    {
        ArgumentNullException.ThrowIfNull(grains);

        _grains = grains;
        Owner = owner;
    }

    internal OwnerId Owner { get; }

    internal NeuronId Root => IBrainNeuron.ForOwner(Owner);

    internal Task ActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Brain().Activate().WaitAsync(cancellationToken);
    }

    internal NeuronReference<TNeuron> GetReference<TNeuron>(
        DigitalBrainClient client,
        string name)
        where TNeuron : INeuron
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        return new NeuronReference<TNeuron>(client, name);
    }

    internal TNeuron GetGrainProxy<TNeuron>(string name)
        where TNeuron : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        return _grains.GetGrain<TNeuron>(NeuronId.For<TNeuron>(Owner, name).ToGrainId());
    }

    internal TEntity GetEntity<TEntity>(string name)
        where TEntity : class, IEntity
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainEntityContract(typeof(TEntity));
        return _grains.GetGrain<TEntity>(EntityId.For<TEntity>(Owner, name).ToGrainId());
    }

    internal Task<DeliveryOutcome> SendAsync<TNeuron>(
        string name,
        Signal signal,
        CancellationToken cancellationToken)
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        return SendAsync(NeuronId.For<TNeuron>(Owner, name), signal, cancellationToken);
    }

    internal async Task<DeliveryOutcome> SendAsync(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken)
        => (await SendResultAsync(receiver, signal, cancellationToken).ConfigureAwait(false)).Outcome;

    internal async Task<TResponse> SendRequestAsync<TResponse>(
        NeuronId receiver,
        Signal request,
        CancellationToken cancellationToken)
        where TResponse : Signal
    {
        var response = await SendRequestAsync(
            receiver,
            request,
            typeof(TResponse),
            cancellationToken).ConfigureAwait(false);
        return (TResponse)response;
    }

    internal Task<JournalRead> ReadJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        RequireOwnedSubject(subject);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        cancellationToken.ThrowIfCancellationRequested();
        return Brain()
            .ReadNeuronJournal(subject, kind, afterSequence)
            .WaitAsync(cancellationToken);
    }

    internal Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        NeuronId subject,
        CancellationToken cancellationToken)
    {
        RequireOwnedSubject(subject);
        cancellationToken.ThrowIfCancellationRequested();
        return Brain().ReadNeuronSynapses(subject).WaitAsync(cancellationToken);
    }

    internal async IAsyncEnumerable<JournalRead> WatchJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequireOwnedSubject(subject);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCreateJournalObserver(kind, out var observer, out var reference))
        {
            var cursor = afterSequence;
            while (!cancellationToken.IsCancellationRequested)
            {
                var page = await Brain()
                    .ReadNeuronJournal(subject, kind, cursor)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (page.Delta.Count > 0 || page.ResetSnapshot is not null)
                {
                    yield return page;
                }

                cursor = page.ResumeSequence;
                await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
            }

            yield break;
        }

        var brain = Brain();
        try
        {
            await brain
                .WatchNeuron(subject, kind, afterSequence, reference)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            await foreach (var page in observer.Reads
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return page;
            }
        }
        finally
        {
            await TeardownWatchAsync(brain, subject, reference, observer).ConfigureAwait(false);
        }
    }

    private IBrainNeuron Brain()
        => _grains.GetGrain<IBrainNeuron>(Root.ToGrainId());

    private async Task<SignalDeliveryResult> SendResultAsync(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken)
    {
        RequireOwnedSubject(receiver);
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        await ActivateAsync(cancellationToken).ConfigureAwait(false);
        return await Brain().Send(receiver, signal)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Signal> SendRequestAsync(
        NeuronId receiver,
        Signal request,
        Type responseType,
        CancellationToken cancellationToken)
    {
        RequireOwnedSubject(receiver);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseType);
        if (!typeof(Signal).IsAssignableFrom(responseType)
            || responseType.IsAbstract
            || responseType.IsInterface)
        {
            throw new ArgumentException(
                $"Response type '{responseType}' must be a concrete Signal.",
                nameof(responseType));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var brain = Brain();
        var cursor = await brain
            .ReadNeuronJournal(Root, JournalKind.Incoming, afterSequence: long.MaxValue)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!TryCreateJournalObserver(JournalKind.Incoming, out var observer, out var reference))
        {
            var result = await SendResultAsync(receiver, request, cancellationToken).ConfigureAwait(false);
            RequireHandled(receiver, request, result.Outcome);
            return await PollForResponseAsync(
                brain,
                Root,
                cursor.ResumeSequence,
                result.Delivery.CorrelationId,
                responseType,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await brain
                .WatchNeuron(Root, JournalKind.Incoming, cursor.ResumeSequence, reference)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await SendResultAsync(receiver, request, cancellationToken).ConfigureAwait(false);
            RequireHandled(receiver, request, result.Outcome);
            return await WaitForResponseAsync(
                observer,
                result.Delivery.CorrelationId,
                responseType,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await TeardownWatchAsync(brain, Root, reference, observer).ConfigureAwait(false);
        }
    }

    private void RequireOwnedSubject(NeuronId subject)
    {
        if (subject.Owner != Owner)
        {
            throw new NeuronAuthorizationException(
                $"Client owner '{Owner}' cannot access neuron '{subject}' owned by '{subject.Owner}'.");
        }
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

    private static void RequireHandled(
        NeuronId receiver,
        Signal request,
        DeliveryOutcome outcome)
    {
        switch (outcome)
        {
            case DeliveryOutcome.Handled:
                return;
            case DeliveryOutcome.Unhandled:
                throw new InvalidOperationException(
                    $"Neuron '{receiver}' did not handle request '{request.GetType().Name}'.");
            case DeliveryOutcome.Refused:
                throw new SignalDeliveryRefusedException(receiver, request.GetType());
            default:
                throw new InvalidOperationException(
                    $"Neuron '{receiver}' returned unknown delivery outcome '{outcome}'.");
        }
    }

    private static async Task<Signal> WaitForResponseAsync(
        ChannelJournalObserver observer,
        CorrelationId correlation,
        Type responseType,
        CancellationToken cancellationToken)
    {
        await foreach (var page in observer.Reads.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.CorrelationId == correlation
                    && responseType.IsInstanceOfType(delivery.Signal))
                {
                    return delivery.Signal;
                }
            }
        }

        throw new InvalidOperationException(
            $"The root journal watch ended before a '{responseType.Name}' response arrived for correlation '{correlation}'.");
    }

    private static async Task<Signal> PollForResponseAsync(
        IBrainNeuron brain,
        NeuronId root,
        long cursor,
        CorrelationId correlation,
        Type responseType,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var read = await brain
                .ReadNeuronJournal(root, JournalKind.Incoming, cursor)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var candidate in read.Delta)
            {
                if (candidate.CorrelationId == correlation
                    && responseType.IsInstanceOfType(candidate.Signal))
                {
                    return candidate.Signal;
                }
            }

            if (read.ResetSnapshot is not null)
            {
                throw new InvalidOperationException(
                    $"The root journal compacted past sequence {cursor} before a "
                    + $"'{responseType.Name}' response arrived for correlation '{correlation}'.");
            }

            cursor = read.ResumeSequence;
            await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TeardownWatchAsync(
        IBrainNeuron brain,
        NeuronId subject,
        IJournalObserver reference,
        ChannelJournalObserver observer)
    {
        try
        {
            await brain.UnwatchNeuron(subject, reference).ConfigureAwait(false);
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
            || typeof(IBrainNeuron).IsAssignableFrom(neuronType))
        {
            throw new NeuronAuthorizationException(
                $"'{neuronType.Name}' is not addressable through IDigitalBrain.Get. Activate the brain with ActivateAsync; address domain neuron contracts with Get; send signals through SendAsync; observe the owner root through IDigitalBrain and domain neurons through NeuronReference.");
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
