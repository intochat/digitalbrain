namespace DigitalBrain.AI;

[GenerateSerializer, Alias("assistant.model-catalog")]
public sealed record ModelCatalog([property: Id(0)] ModelChoice Automatic, [property: Id(1)] IReadOnlyList<ModelChoice> Models);

[GenerateSerializer, Alias("assistant.model-choice")]
public sealed record ModelChoice([property: Id(0)] string? Id, [property: Id(1)] string Label, [property: Id(2)] string? Provider, [property: Id(3)] string? Model, [property: Id(4)] bool Available, [property: Id(5)] IReadOnlyList<string> Capabilities);
