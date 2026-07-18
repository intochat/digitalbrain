using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai;

[GenerateSerializer]
public sealed record Voice2TextRequest([property: Id(1)] byte[] Audio,
    [property: Id(2)] string MimeType,
    [property: Id(3)] string? LanguageHint,
    [property: Id(4)] bool ReturnSegments
) : Synapse;
