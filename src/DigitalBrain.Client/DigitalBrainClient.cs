using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

public sealed class DigitalBrainClient : IDigitalBrain
{
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

    public T Get<T>(string name)
        where T : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(T));

        return _grains.GetGrain<T>(NeuronId.For<T>(Owner, name).ToGrainId());
    }

    public Task SendAsync<TNeuron>(string name, Synapse synapse)
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));

        return SendAsync(
            new NeuronId(NeuronId.GrainTypeNameOf(typeof(TNeuron)), Owner, name),
            synapse);
    }

    public Task SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        if (receiver.Owner != Owner)
        {
            throw new NeuronAuthorizationException(
                $"Client owner '{Owner}' cannot send to neuron '{receiver}' owned by '{receiver.Owner}'.");
        }

        if (string.Equals(receiver.Type, ISessionNeuron.GrainTypeName, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException(
                "The owner session is the client entry gateway, not a Send target. Use SendAsync to domain neurons and EmitAsync to broadcast.");
        }

        return Session().Fire(receiver, synapse);
    }

    public Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return Session().Emit(synapse);
    }

    private ISessionNeuron Session()
        => _grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(Owner).ToGrainId());

    private static void RequireDomainNeuronContract(Type neuronType)
    {
        if (neuronType == typeof(INeuron)
            || typeof(ISessionNeuron).IsAssignableFrom(neuronType))
        {
            throw new NeuronAuthorizationException(
                $"'{neuronType.Name}' is not addressable through IDigitalBrain. Address domain neuron contracts with Get; fire and emit through SendAsync and EmitAsync. Journal observation is not an IDigitalBrain API.");
        }
    }
}
