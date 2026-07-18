using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record BrainstormOption(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] string Summary,
    [property: Id(3)] IReadOnlyList<string> Participants);

[GenerateSerializer]
public sealed record BrainstormOptions([property: Id(1)] string Prompt,
    [property: Id(2)] IReadOnlyList<BrainstormOption> Options
) : Synapse;
