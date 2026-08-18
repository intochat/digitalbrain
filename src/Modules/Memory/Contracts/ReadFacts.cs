namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.read-facts")]
public sealed record ReadFacts(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string? Kind = null,
    [property: Id(2)] string? Correlation = null,
    [property: Id(3)] int Limit = 100) : RequestSynapse<FactsRead>;
