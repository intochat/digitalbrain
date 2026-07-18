using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Runtime;

[GenerateSerializer]
public sealed record ResourceFailed(
    [property: Id(0)] string ResourceName,
    [property: Id(1)] int ExitCode,
    [property: Id(2)] string ErrorSummary,
    [property: Id(3)] string Logs
) : Synapse;
