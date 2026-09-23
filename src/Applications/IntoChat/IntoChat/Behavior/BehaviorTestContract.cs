using System.Text.RegularExpressions;

namespace IntoChat;

// A behavior's tests must exercise the behavior itself. A suite that never names a type declared
// by the behavior source proves nothing, so the developer-tier check rejects it before compiling.
internal static partial class BehaviorTestContract
{
    public static void RejectTestsThatNeverReferenceTheBehavior(string source, string tests)
    {
        var declared = DeclaredTypes(source);
        if (declared.Count == 0) { return; }
        if (declared.Any(name => Regex.IsMatch(tests, $@"\b{Regex.Escape(name)}\b"))) { return; }
        throw new ArgumentException("The tests never reference the behavior under test. Exercise the behavior's own types or methods instead of a tautology.");
    }

    private static IReadOnlyList<string> DeclaredTypes(string source)
        => TypeDeclaration().Matches(source).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

    [GeneratedRegex(@"(?:class|record|struct|interface)\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex TypeDeclaration();
}
