using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.behavior-status")]
public enum BehaviorStatus
{
    Admitted,
    Running,
    Completed,
    Failed,
}

// Current program and its latest execution outcome. This is durable domain state;
// the bounded signal journal is only a record of recent changes.
[GenerateSerializer]
[Alias("db.behavior-definition")]
public sealed record BehaviorDefinition(
    [property: Id(0)] string Name,
    [property: Id(1)] string Source,
    [property: Id(2)] Guid Revision,
    [property: Id(3)] BehaviorStatus Status,
    [property: Id(4)] string Summary,
    [property: Id(5)] IReadOnlyList<string> Diagnostics,
    [property: Id(6)] PrincipalId? Principal = null);
