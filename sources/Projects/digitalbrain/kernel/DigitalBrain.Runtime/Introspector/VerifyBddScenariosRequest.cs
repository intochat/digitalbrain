using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record VerifyBddScenariosRequest([property: Id(1)] string InoSource
) : Synapse;
