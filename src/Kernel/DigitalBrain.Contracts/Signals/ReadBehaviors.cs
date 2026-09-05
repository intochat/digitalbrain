namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.read-behaviors")]
public sealed record ReadBehaviors : Signal<BehaviorsRead>;

[GenerateSerializer]
[Alias("db.behaviors-read")]
public sealed record BehaviorsRead(
    [property: Id(0)] IReadOnlyList<BehaviorDefinition> Behaviors) : Signal;
