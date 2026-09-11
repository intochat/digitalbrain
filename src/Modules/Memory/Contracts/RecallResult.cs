namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.recall-result")]
public sealed record RecallResult(
    [property: Id(0)] IReadOnlyList<RecalledMemory> Matches);
