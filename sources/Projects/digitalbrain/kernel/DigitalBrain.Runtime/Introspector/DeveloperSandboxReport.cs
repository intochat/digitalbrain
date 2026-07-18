using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

/// <summary>
/// Synapse containing the E2E verification report for the developer sandbox creation.
/// </summary>
[GenerateSerializer]
public sealed record DeveloperSandboxReport(
    [property: Id(1)] bool Success,
    [property: Id(2)] string Message,
    [property: Id(3)] string CreatedPath
) : Synapse;
