namespace DigitalBrain.Ino;

public enum InoCapabilityQueryKind
{
    None,
    Inventory,
    SpecificKnown,
    SpecificUnknown
}

public sealed record InoCapabilityQuery(InoCapabilityQueryKind Kind, InoCapabilityRecord? Capability = null, string? RequestedName = null);

public static partial class InoCapabilityAnswers
{
    public static InoCapabilityQuery TryParseQuery(string prompt, IReadOnlyList<InoCapabilityRecord> capabilities)
    {
        var mentioned = capabilities.FirstOrDefault(capability => capability.Matches(prompt));
        if (mentioned is not null && LooksLikeCapabilityStatus(prompt))
        {
            return new InoCapabilityQuery(InoCapabilityQueryKind.SpecificKnown, mentioned, mentioned.Id);
        }

        var unknown = CapabilityNameRegex().Match(prompt);
        if (unknown.Success)
        {
            return new InoCapabilityQuery(InoCapabilityQueryKind.SpecificUnknown, RequestedName: unknown.Groups["name"].Value.Trim());
        }

        return InventoryRegex().IsMatch(prompt)
            ? new InoCapabilityQuery(InoCapabilityQueryKind.Inventory)
            : new InoCapabilityQuery(InoCapabilityQueryKind.None);
    }

    public static bool TryCreateAnswer(
        string prompt,
        IReadOnlyList<InoCapabilityRecord> capabilities,
        out string answer)
    {
        answer = string.Empty;
        var query = TryParseQuery(prompt, capabilities);
        switch (query.Kind)
        {
            case InoCapabilityQueryKind.SpecificKnown when query.Capability is not null:
                answer = $"Yes. {query.Capability.DisplayName} is registered from {query.Capability.SourceKind} ({query.Capability.Origin}). " +
                         $"{query.Capability.Description} Known aliases: {string.Join(", ", query.Capability.Aliases)}.";
                return true;
            case InoCapabilityQueryKind.SpecificUnknown:
                answer = $"No. I do not have a registered capability for '{query.RequestedName}'. I will not claim or use unregistered integrations.";
                return true;
            case InoCapabilityQueryKind.Inventory:
                answer = InventoryAnswer(capabilities);
                return true;
            default:
                return false;
        }
    }

    private static string InventoryAnswer(IReadOnlyList<InoCapabilityRecord> capabilities)
    {
        var lines = new List<string> { "Registered capabilities I can use without guessing:" };
        foreach (var record in capabilities.OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {record.DisplayName} ({record.Id}, source: {record.SourceKind}, trust: {record.TrustLevel}): {record.Description}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool LooksLikeCapabilityStatus(string prompt) =>
        StatusRegex().IsMatch(prompt) || prompt.TrimEnd().EndsWith('?');

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(available|capabilit(?:y|ies)|support|registered|access|have|use)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex StatusRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:what\s+can\s+you\s+do|capabilit(?:y|ies)|available\s+system)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex InventoryRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:do\s+you\s+have|can\s+(?:you|i)\s+(?:use|access)|is)\s+(?:access\s+to\s+)?(?:a|an)?\s*(?<name>[A-Za-z][A-Za-z0-9 ._-]*?)\??$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex CapabilityNameRegex();
}
