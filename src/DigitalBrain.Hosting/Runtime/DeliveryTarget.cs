namespace DigitalBrain;

internal sealed record DeliveryTarget(string Kind, string Name)
{
    internal static DeliveryTarget From(NeuronId id) => new(id.Kind, id.Name);

    internal NeuronId ToNeuronId() => new(Kind, Name);
}
