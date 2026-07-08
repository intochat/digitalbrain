using System.Text.RegularExpressions;

namespace DigitalBrain.Ino;

internal static partial class InoPromptSemantics
{
    public static bool MatchesCapability(string prompt, IReadOnlyList<InoCapabilityRecord> capabilities, string capabilityId) =>
        capabilities.Any(capability =>
            string.Equals(capability.Id, capabilityId, StringComparison.OrdinalIgnoreCase) &&
            capability.Matches(prompt));

    public static bool HasAll(string prompt, params string[] tokens)
    {
        var promptTokens = Tokenize(prompt);
        return tokens.Select(InoAgentCapabilities.NormalizeId).All(promptTokens.Contains);
    }

    public static bool HasAny(string prompt, params string[] tokens)
    {
        var promptTokens = Tokenize(prompt);
        return tokens.Select(InoAgentCapabilities.NormalizeId).Any(promptTokens.Contains);
    }

    public static int? ResultCount(string prompt)
    {
        var match = NumberRegex().Match(prompt);
        if (match.Success && int.TryParse(match.Value, out var explicitCount))
        {
            return Math.Clamp(explicitCount, 1, 50);
        }

        return HasAny(prompt, "last", "latest") || HasAll(prompt, "most", "recent") ? 1 : null;
    }

    public static string? TryExtractFromQuery(string prompt)
    {
        var match = FromQueryRegex().Match(prompt);
        return match.Success ? match.Groups["query"].Value.Trim() : null;
    }

    private static HashSet<string> Tokenize(string prompt) =>
        WordRegex().Matches(prompt ?? string.Empty)
            .Select(match => InoAgentCapabilities.NormalizeId(match.Value))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\b\d{1,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\bfrom\s+(?<query>[A-Za-z0-9._%+\-@]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FromQueryRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-@]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
