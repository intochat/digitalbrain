namespace DigitalBrain.Coding;

// Detail names the edit a check or commit refused on ("edit 2 (ReplaceMember M:...) left 1 error: ...").
[GenerateSerializer]
[Alias("coding.change-set-snapshot")]
public sealed record ChangeSetSnapshot(
    [property: Id(0)] ChangeSetStatus Status,
    [property: Id(1)] IReadOnlyList<EditRequest> Edits,
    [property: Id(2)] IReadOnlyList<DiagnosticHit> Diagnostics,
    [property: Id(3)] string? Diff,
    [property: Id(4)] long Generation,
    [property: Id(5)] string? Detail,
    [property: Id(6)] IReadOnlyList<string> Files,
    // Bumped by every reaction that saves (propose, check, commit - success or failure - and discard), so a
    // caller that read the change set before issuing a command can wait for exactly this command's own
    // settle, never a leftover Detail/Status a previous command already produced.
    [property: Id(7)] int Revision);