namespace DigitalBrain.Memory;

/// <summary>A query for matching notes.</summary>
[GenerateSerializer]
[Alias("memory.recall")]
public sealed record Recall(
    [property: Id(0)] string Namespace,
    [property: Id(1)] string Query,
    [property: Id(2)] int Limit,
    [property: Id(3)] IReadOnlyList<MemoryTag> Tags);
