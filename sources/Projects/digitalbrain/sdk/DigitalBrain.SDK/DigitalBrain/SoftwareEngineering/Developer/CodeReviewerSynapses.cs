using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

[GenerateSerializer]
public sealed record ReviewCodeRequest([property: Id(1)] string Diff,
    [property: Id(2)] string? TargetFile = null
) : Synapse;

[GenerateSerializer]
public sealed record ReviewCodeResponse([property: Id(1)] bool Approved,
    [property: Id(2)] string Feedback,
    [property: Id(3)] string? ErrorMessage = null
) : Synapse;
