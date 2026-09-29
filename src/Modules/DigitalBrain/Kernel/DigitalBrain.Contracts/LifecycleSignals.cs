namespace DigitalBrain.Contracts;

[GenerateSerializer, Alias("brain.module-loaded")]
public sealed record ModuleLoaded([property: Id(0)] string ModuleType) : Signal;

[GenerateSerializer, Alias("brain.neuron-activity")]
public abstract record NeuronActivity : Signal
{
    [Id(0)] public required string NeuronId { get; init; }
    [Id(1)] public required string Key { get; init; }
    [Id(2)] public required IReadOnlyList<string> TypeIds { get; init; }
    [Id(3)] public required Guid ActivationId { get; init; }
    [Id(4)] public required DateTimeOffset ObservedAt { get; init; }
}

[GenerateSerializer, Alias("brain.neuron-activated")]
public sealed record NeuronActivated : NeuronActivity;

[GenerateSerializer, Alias("brain.neuron-deactivated")]
public sealed record NeuronDeactivated : NeuronActivity;