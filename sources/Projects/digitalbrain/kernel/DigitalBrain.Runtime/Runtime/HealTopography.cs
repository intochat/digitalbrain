using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Runtime;

[GenerateSerializer]
public sealed record FailedResourceDetails(
    [property: Id(0)] string ResourceName,
    [property: Id(1)] string State,
    [property: Id(2)] int? ExitCode,
    [property: Id(3)] string ErrorSummary,
    [property: Id(4)] string Logs
);

[GenerateSerializer]
public sealed record HealTopographyRequest(
    [property: Id(0)] IReadOnlyList<FailedResourceDetails> FailedResources
) : Synapse;

[GenerateSerializer]
public sealed record HealTopographyResponse(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Summary,
    [property: Id(2)] IReadOnlyList<string> FixedResources,
    [property: Id(3)] IReadOnlyList<string> UnfixableResources
) : Synapse;
