using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;

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

    public static Task<BehaviorBrain<TTrigger>> ConnectAsync<TTrigger>(
        CancellationToken cancellationToken = default)
        where TTrigger : Synapse
    {
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Task.FromException<BehaviorBrain<TTrigger>>(
            new InvalidOperationException(
                "DigitalBrainClient.ConnectAsync is supplied by the isolated behavior worker."));
    }

    public Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Brain().Activate();
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

    public Task SendAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken = default)
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return Get<TNeuron>(name).SendAsync(synapse, cancellationToken);
    }

    public async Task SendAsync(NeuronId receiver, Synapse synapse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (receiver.Owner != Owner)
        {
            throw new NeuronAuthorizationException(
                $"Client owner '{Owner}' cannot send to neuron '{receiver}' owned by '{receiver.Owner}'.");
        }

        if (string.Equals(receiver.Type, ISessionNeuron.GrainTypeName, StringComparison.Ordinal)
            || string.Equals(receiver.Type, IDigitalBrainNeuron.GrainTypeName, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException(
                "The owner DigitalBrain and session are not Send targets. Use ActivateAsync, domain Get, SendAsync to domain neurons, and EmitAsync to broadcast.");
        }

        await SendToAsync(receiver, synapse, cancellationToken);
    }

    public async Task EmitAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        await ActivateAsync(cancellationToken);
        await Session().Emit(synapse);
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
        var cursor = (await session.ReadNeuronJournal(sessionId, JournalKind.Incoming, afterSequence: 0))
            .ResumeSequence;

        var delivery = await SendValidatedAsync(receiver, request, cancellationToken);

        while (true)
        {
            var read = await session.ReadNeuronJournal(sessionId, JournalKind.Incoming, cursor);
            foreach (var candidate in read.Delta)
            {
                if (candidate.CorrelationId == delivery.CorrelationId
                    && responseType.IsInstanceOfType(candidate.Synapse))
                {
                    return candidate.Synapse;
                }
            }

            if (read.ResetSnapshot is not null)
            {
                throw new InvalidOperationException(
                    $"The session journal compacted past sequence {cursor} before a "
                    + $"'{responseType.Name}' response arrived for correlation '{delivery.CorrelationId}'.");
            }

            cursor = read.ResumeSequence;
            await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private IDigitalBrainNeuron Brain()
        => _grains.GetGrain<IDigitalBrainNeuron>(IDigitalBrainNeuron.ForOwner(Owner).ToGrainId());

    private ISessionNeuron Session()
        => _grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(Owner).ToGrainId());

    private async Task<SynapseDelivery> SendValidatedAsync(
        NeuronId receiver,
        Synapse synapse,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ActivateAsync(cancellationToken);
        return await Session().Fire(receiver, synapse);
    }

    private static void RequireDomainNeuronContract(Type neuronType)
    {
        if (neuronType == typeof(INeuron)
            || typeof(ISessionNeuron).IsAssignableFrom(neuronType)
            || typeof(IDigitalBrainNeuron).IsAssignableFrom(neuronType)
            || typeof(IBehavior).IsAssignableFrom(neuronType))
        {
            throw new NeuronAuthorizationException(
                $"'{neuronType.Name}' is not addressable through IDigitalBrain.Get. Activate the brain with ActivateAsync; address domain neuron contracts with Get; fire and emit through SendAsync and EmitAsync. Journal observation is not an IDigitalBrain API.");
        }
    }
}
