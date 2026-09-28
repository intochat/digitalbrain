namespace DigitalBrain.Microsoft.Roslyn;

// One edit against the current snapshot. Each kind reads the fields it needs:
// ReplaceMember: SymbolId, Source. InsertMember: SymbolId (a type, or the member to insert after), Source.
// AddUsing: Path, Namespace. ReplaceRange: Path, StartLine, EndLine, Source. Rename: SymbolId, NewName.
// ApplyCodeFix: Path, DiagnosticId, optional StartLine and FixTitle.
[GenerateSerializer]
[Alias("coding.edit-request")]
public sealed record EditRequest(
    [property: Id(0)] EditKind Kind,
    [property: Id(1)] string? SymbolId = null,
    [property: Id(2)] string? Path = null,
    [property: Id(3)] string? Source = null,
    [property: Id(4)] string? NewName = null,
    [property: Id(5)] int? StartLine = null,
    [property: Id(6)] int? EndLine = null,
    [property: Id(7)] string? Namespace = null,
    [property: Id(8)] string? DiagnosticId = null,
    [property: Id(9)] string? FixTitle = null);