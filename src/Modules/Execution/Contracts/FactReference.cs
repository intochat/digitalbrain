using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.fact-reference")]
public readonly record struct FactReference(
    [property: Id(0)] NeuronId Source,
    [property: Id(1)] SynapseId Fact);
