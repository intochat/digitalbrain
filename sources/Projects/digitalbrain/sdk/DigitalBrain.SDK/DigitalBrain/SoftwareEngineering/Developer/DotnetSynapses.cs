using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

[GenerateSerializer]
public sealed record DotnetRequest([property: Id(1)] string Command,
    [property: Id(2)] string? Arguments = null
) : Synapse;

[GenerateSerializer]
public sealed record DotnetResponse([property: Id(1)] bool Success,
    [property: Id(2)] int ExitCode,
    [property: Id(3)] string Output,
    [property: Id(4)] string? ErrorMessage = null
) : Synapse;
