using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record Voice2TextResponse([property: Id(1)] string Transcript,
    [property: Id(2)] string DetectedLanguage,
    [property: Id(3)] IReadOnlyList<Voice2TextSegment> Segments
) : Synapse;

[GenerateSerializer]
public sealed record Voice2TextSegment(
    [property: Id(0)] TimeSpan Start,
    [property: Id(1)] TimeSpan End,
    [property: Id(2)] string Text);
