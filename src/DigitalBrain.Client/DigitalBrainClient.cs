using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

public sealed class DigitalBrainClient : IDigitalBrain
{
    private const string SessionName = "session";

    private readonly IGrainFactory _grains;

    private DigitalBrainClient(IGrainFactory grains, OwnerId owner)
    {
        _grains = grains;
        Owner = owner;
    }

    public OwnerId Owner { get; }

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

        return _grains.GetGrain<T>(NeuronId.For<T>(Owner, name).ToGrainId());
    }

    public Task SendAsync<TNeuron>(string name, Synapse synapse)
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

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

        return Session().Fire(receiver, synapse);
    }

    public Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return Session().Emit(synapse);
    }

    private ISessionNeuron Session()
        => _grains.GetGrain<ISessionNeuron>(
            new NeuronId(ISessionNeuron.GrainTypeName, Owner, SessionName).ToGrainId());
}
