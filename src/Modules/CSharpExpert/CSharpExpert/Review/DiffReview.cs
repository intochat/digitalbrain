using System.Text.RegularExpressions;

namespace DigitalBrain.CSharpExpert;

public static partial class DiffReview
{
    private const int CommentDensityMinimum = 3;
    private const double MaxCommentShare = 0.2;
    private static readonly HashSet<string> AllowedShortNames = ["i", "j", "k", "_", "x", "y", "id"];

    public static IReadOnlyList<string> Check(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var added = AddedLines(diff);
        var findings = new List<string>();
        findings.AddRange(added.Where(line => line.Contains("/// <summary>", StringComparison.Ordinal))
            .Select(line => $"Remove the doc comment: {line.Trim()}"));

        var comments = added.Count(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("///", StringComparison.Ordinal));
        if (comments >= CommentDensityMinimum && comments > added.Count * MaxCommentShare)
        {
            findings.Add($"Too many inline comments ({comments} of {added.Count} added lines); let names explain the code.");
        }

        findings.AddRange(added
            .SelectMany(line => LocalDeclaration().Matches(line).Select(match => match.Groups["name"].Value))
            .Where(name => name.Length <= 2 && !AllowedShortNames.Contains(name))
            .Distinct()
            .Select(name => $"Rename '{name}' to a self-explanatory name."));
        return findings;
    }

    private static List<string> AddedLines(string diff)
        => diff.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            .Select(line => line[1..])
            .Where(line => line.Trim().Length > 0)
            .ToList();

    [GeneratedRegex(@"\b(?:var|int|long|string|bool|double|decimal|object)\s+(?<name>[A-Za-z_]\w*)\s*(?:=|;|,|\))")]
    private static partial Regex LocalDeclaration();
}
