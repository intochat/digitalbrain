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

    public Task ActivateAsync()
        => Brain().Activate();

    public T Get<T>(string name)
        where T : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(T));

        return _grains.GetGrain<T>(NeuronId.For<T>(Owner, name).ToGrainId());
    }

    public async Task SendAsync<TNeuron>(string name, Synapse synapse)
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDomainNeuronContract(typeof(TNeuron));
        ArgumentNullException.ThrowIfNull(synapse);

        await SendValidatedAsync(new NeuronId(NeuronId.GrainTypeNameOf(typeof(TNeuron)), Owner, name), synapse);
    }

    public async Task SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

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

        await SendValidatedAsync(receiver, synapse);
    }

    public async Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        await ActivateAsync();
        await Session().Emit(synapse);
    }

    private IDigitalBrainNeuron Brain()
        => _grains.GetGrain<IDigitalBrainNeuron>(IDigitalBrainNeuron.ForOwner(Owner).ToGrainId());

    private ISessionNeuron Session()
        => _grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(Owner).ToGrainId());

    private async Task SendValidatedAsync(NeuronId receiver, Synapse synapse)
    {
        await ActivateAsync();
        await Session().Fire(receiver, synapse);
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
