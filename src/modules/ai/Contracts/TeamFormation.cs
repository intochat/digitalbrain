namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.team-formation")]
public sealed record TeamFormation([property: Id(0)] IReadOnlyList<string> Models);
