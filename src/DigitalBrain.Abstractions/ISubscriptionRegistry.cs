namespace DigitalBrain.Abstractions;

[Alias("db.subscription-registry")]
public interface ISubscriptionRegistry : IGrainWithStringKey
{
    [Alias(nameof(Register))]
    Task Register(string synapseType, NeuronId subscriber);

    [Alias(nameof(Subscribers))]
    Task<IReadOnlyList<NeuronId>> Subscribers(string synapseType);
}
