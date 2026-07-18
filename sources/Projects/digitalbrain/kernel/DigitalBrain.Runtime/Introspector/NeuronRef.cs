namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record NeuronRef(
    [property: Id(0)] string NeuronType,
    [property: Id(1)] string Domain,
    [property: Id(2)] string? FeatureSnippet);
