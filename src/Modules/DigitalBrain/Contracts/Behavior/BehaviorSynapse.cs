namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.synapse")]
public sealed record BehaviorSynapse([property: Id(0)] string From, [property: Id(1)] string To);
