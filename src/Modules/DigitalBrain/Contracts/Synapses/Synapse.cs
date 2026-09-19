using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Abstractions.Synapses;

[GenerateSerializer]
[Alias("db.synapse")]
public sealed record Synapse(
    [property: Id(0)] INeuron Source,
    [property: Id(1)] INeuron Target,
    [property: Id(2)] string SignalType,
    [property: Id(3)] DateTimeOffset CreatedAt)
{
    public override string ToString() => $"{Source.GetGrainId()} --{SignalType}--> {Target.GetGrainId()}";
}
