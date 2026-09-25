using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

public static class EditNormalizer
{
    // Models often name the member they are about to create; an insert needs the type that will hold it.
    public static IReadOnlyList<EditRequest> Normalize(IReadOnlyList<EditRequest> edits)
        => [.. edits.Select(edit => edit.Kind == EditKind.InsertMember && edit.SymbolId is { } id && !id.StartsWith("T:", StringComparison.Ordinal)
            ? edit with { SymbolId = ContainingType(id) }
            : edit)];

    private static string ContainingType(string memberId)
    {
        var name = memberId[(memberId.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var parameters = name.IndexOf('(', StringComparison.Ordinal);
        if (parameters >= 0)
        {
            name = name[..parameters];
        }

        var lastDot = name.LastIndexOf('.');
        return lastDot > 0 ? "T:" + name[..lastDot] : memberId;
    }
}
