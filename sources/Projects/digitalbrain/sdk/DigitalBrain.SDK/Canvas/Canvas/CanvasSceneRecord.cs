namespace DigitalBrain.SDK.Canvas.Canvas;

[GenerateSerializer]
public sealed record CanvasSceneRecord(
    [property: Id(0)] string UserId,
    [property: Id(1)] string SceneName,
    [property: Id(2)] string Content,
    [property: Id(3)] DateTimeOffset UpdatedUtc);
