using Orleans;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>Orleans wire form of <see cref="NeuronId"/>; the core stays Orleans-free.</summary>
[GenerateSerializer]
internal struct NeuronIdSurrogate
{
    [Id(0)]
    public string Value;
}

[RegisterConverter]
internal sealed class NeuronIdSurrogateConverter : IConverter<NeuronId, NeuronIdSurrogate>
{
    public NeuronId ConvertFromSurrogate(in NeuronIdSurrogate surrogate)
        => string.IsNullOrEmpty(surrogate.Value) ? default : NeuronId.Parse(surrogate.Value);

    public NeuronIdSurrogate ConvertToSurrogate(in NeuronId value) => new() { Value = value.ToString() };
}
