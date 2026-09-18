using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.definition")]
public sealed record BehaviorDefinition(
    [property: Id(0)] string Id,
    [property: Id(1)] string Name,
    [property: Id(2)] string Trigger,
    [property: Id(3)] IReadOnlyList<BehaviorNode> Nodes,
    [property: Id(4)] IReadOnlyList<BehaviorSynapse> Synapses,
    [property: Id(5)] string? Description = null);
