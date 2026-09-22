using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.change-set-state")]
internal sealed record ChangeSetState(
    [property: Id(0)] ChangeSetStatus Status,
    [property: Id(1)] IReadOnlyList<EditRequest> Edits,
    [property: Id(2)] IReadOnlyList<DiagnosticHit> Diagnostics,
    [property: Id(3)] string? Diff,
    [property: Id(4)] long Generation,
    [property: Id(5)] string? Detail,
    [property: Id(6)] IReadOnlyList<string> Files,
    [property: Id(7)] int Revision)
{
    public static readonly ChangeSetState Empty = new(ChangeSetStatus.Draft, [], [], null, 0, null, [], 0);
}