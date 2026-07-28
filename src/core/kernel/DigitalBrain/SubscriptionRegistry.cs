using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Kernel;

internal sealed class SubscriptionRegistry : DurableGrain, ISubscriptionRegistry
{
    private const string SubscribersStateName = "subscribers";

    private readonly IDurableDictionary<string, byte[]> _subscribers;
    private readonly Serializer<NeuronId[]> _neurons;

    public SubscriptionRegistry()
    {
        _subscribers = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>(SubscribersStateName);
        _neurons = ServiceProvider.GetRequiredService<Serializer<NeuronId[]>>();
    }

    internal OwnerId Owner => new(this.GetPrimaryKeyString());

    public async Task Register(string synapseType, NeuronId subscriber)
    {
        if (subscriber.Owner != Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{subscriber}' belongs to owner '{subscriber.Owner}' and cannot subscribe in owner '{Owner}'s registry.");
        }

        var registered = Read(synapseType);

        if (registered.Contains(subscriber))
        {
            return;
        }

        _subscribers[synapseType] = _neurons.SerializeToArray([.. registered, subscriber]);
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<NeuronId>> Subscribers(string synapseType)
        => Task.FromResult<IReadOnlyList<NeuronId>>(Read(synapseType));

    private NeuronId[] Read(string synapseType)
        => _subscribers.TryGetValue(synapseType, out var registered) ? _neurons.Deserialize(registered) : [];

    internal static ISubscriptionRegistry For(IGrainFactory grains, OwnerId owner)
        => grains.GetGrain<ISubscriptionRegistry>(owner.Value);
}
