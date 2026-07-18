using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record VerifyBddScenariosResponse([property: Id(1)] bool Passed,
    [property: Id(2)] string DiagnosticsJson,
    [property: Id(3)] string ScenariosJson
) : Synapse;
