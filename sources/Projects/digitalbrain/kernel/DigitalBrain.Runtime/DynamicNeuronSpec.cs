namespace DigitalBrain.Runtime;

public enum DynamicNeuronStatus
{
    Staged = 0,
    Promoted = 1,
    Retired = 2,
}

[GenerateSerializer]
public sealed record DynamicNeuronSpec(
    [property: Id(0)] NeuronId Id,
    [property: Id(1)] string FeatureText,
    [property: Id(2)] string RoslynScript,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] DynamicNeuronStatus Status);
