using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Canvas;

[GenerateSerializer]
public sealed record CanvasReady([property: Id(1)] string UserId,
    [property: Id(2)] string SceneName,
    [property: Id(3)] string Content
) : Synapse;
