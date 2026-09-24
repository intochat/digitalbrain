namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.part")]
public sealed record AppPart(
    [property: Id(0)] string Name,
    [property: Id(1)] string Behavior,
    [property: Id(2)] IReadOnlyDictionary<string, string> Settings);
