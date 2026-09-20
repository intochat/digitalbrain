namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.state")]
internal sealed record MemoryState(
    [property: Id(0)] int RememberedCount,
    [property: Id(1)] int ForgottenCount,
    [property: Id(2)] DateTimeOffset LastChangedAt);
