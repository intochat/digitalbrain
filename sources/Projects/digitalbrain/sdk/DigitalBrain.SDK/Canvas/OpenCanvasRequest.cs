using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Canvas;

[GenerateSerializer]
public sealed record OpenCanvasRequest([property: Id(1)] string UserId,
    [property: Id(2)] string SceneName
) : Synapse;
