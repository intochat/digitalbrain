using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record IntentClassified([property: Id(1)] string Transcript,
    [property: Id(2)] KnownIntent Intent,
    [property: Id(3)] IReadOnlyDictionary<string,
    string> Parameters
) : Synapse;
