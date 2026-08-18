namespace DigitalBrain.Abstractions.Brain;

[GenerateSerializer]
[Alias("db.brain-graph-connection")]
public sealed record Connection(
    [property: Id(0)] NeuronId From,
    [property: Id(1)] string Role,
    [property: Id(2)] NeuronId To);
