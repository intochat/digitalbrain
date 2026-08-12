namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.workspace.membership")]
public sealed record Membership(
    [property: Id(0)] string Name,
    [property: Id(1)] IReadOnlyList<WorkspaceMember> Members) : Synapse;

