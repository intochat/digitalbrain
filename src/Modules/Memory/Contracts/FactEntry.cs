namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.fact-entry")]
public sealed record FactEntry(
    [property: Id(0)] long Sequence,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? Correlation,
    [property: Id(4)] DateTimeOffset At);
