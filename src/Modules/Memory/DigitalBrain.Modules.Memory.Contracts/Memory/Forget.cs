namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.forget")]
public sealed record Forget(
    [property: Id(0)] string Namespace,
    [property: Id(1)] string Key);