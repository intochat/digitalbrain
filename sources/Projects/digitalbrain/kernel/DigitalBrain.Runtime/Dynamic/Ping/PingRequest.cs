using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic.Ping;

[GenerateSerializer]
public sealed record PingRequest([property: Id(1)] string Text
) : Synapse;
