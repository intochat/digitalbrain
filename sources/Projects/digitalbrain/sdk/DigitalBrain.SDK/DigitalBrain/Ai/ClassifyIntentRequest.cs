using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record ClassifyIntentRequest([property: Id(1)] string Transcript,
    [property: Id(2)] string? Locale
) : Synapse;
