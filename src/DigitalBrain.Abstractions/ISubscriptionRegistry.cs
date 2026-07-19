using Orleans;

namespace DigitalBrain;

[Alias("db.subscription-registry")]
public interface ISubscriptionRegistry : IGrainWithStringKey
{
    [Alias("Register")]
    Task RegisterAsync(string synapseType, NeuronId subscriber);

    [Alias("Subscribers")]
    Task<IReadOnlyList<NeuronId>> SubscribersAsync(string synapseType);

    [Alias("SubscriberCount")]
    Task<int> SubscriberCountAsync(string synapseType);
}
