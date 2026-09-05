namespace DigitalBrain.Abstractions.Signals;

// Remove the saved program and request cooperative cancellation in its host.
[GenerateSerializer]
[Alias("db.remove-behavior")]
public sealed record RemoveBehavior([property: Id(0)] string Name) : Signal;

[GenerateSerializer]
[Alias("db.behavior-removed")]
public sealed record BehaviorRemoved([property: Id(0)] string Name) : Signal;
