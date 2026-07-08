namespace DigitalBrain.Ino;

public static class InoCapabilityAnswers
{
    public static bool IsCapabilityQuestion(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("what can you do") ||
               p.Contains("what are your capabilities") ||
               p.Contains("available system capabilities") ||
               p.Contains("available capabilities") ||
               p.Contains("system capabilities") ||
               p.Contains("list capabilities") ||
               p.Contains("capability status") ||
               p.Contains("capabilities you can use") ||
               p.Contains("which integrations") ||
               p.Contains("do you have ");
    }

    public static bool TryCreateAnswer(
        string prompt,
        IReadOnlyList<InoCapabilityRecord> agentRecords,
        IReadOnlyList<InoIntentClassifier.Capability> projectedCapabilities,
        out string answer)
    {
        answer = string.Empty;
        if (!IsCapabilityQuestion(prompt))
        {
            return false;
        }

        var requested = TryExtractRequestedCapability(prompt);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var agent = agentRecords.FirstOrDefault(record => record.Matches(requested));
            if (agent is not null)
            {
                answer = $"Yes. {agent.DisplayName} is registered from {agent.SourceKind} metadata ({agent.Origin}). " +
                         $"{agent.Description} Known aliases: {string.Join(", ", agent.Aliases)}.";
                return true;
            }

            var projected = projectedCapabilities.FirstOrDefault(cap =>
                cap.Id.Contains(requested, StringComparison.OrdinalIgnoreCase) ||
                requested.Contains(cap.Id, StringComparison.OrdinalIgnoreCase) ||
                cap.Examples.Any(example => example.Contains(requested, StringComparison.OrdinalIgnoreCase)));

            if (projected is not null)
            {
                answer = $"Yes. {projected.Id} is registered in the local capability projection. {projected.Description}";
                return true;
            }

            answer = $"No. I do not have a registered capability for '{requested}'. I will not claim or use unregistered integrations.";
            return true;
        }

        var lines = new List<string> { "Registered capabilities I can use without guessing:" };
        foreach (var record in agentRecords.OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {record.DisplayName} ({record.Id}, source: {record.SourceKind}, trust: {record.TrustLevel}): {record.Description}");
        }

        var agentIds = agentRecords.Select(record => record.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cap in projectedCapabilities
                     .Where(cap => !agentIds.Contains(cap.Id))
                     .OrderBy(cap => cap.Id, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {cap.Id} (projection): {cap.Description}");
        }

        answer = string.Join(Environment.NewLine, lines);
        return true;
    }

    private static string? TryExtractRequestedCapability(string prompt)
    {
        var p = prompt.Trim();
        var marker = p.IndexOf("do you have", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var requested = p[(marker + "do you have".Length)..]
            .Trim()
            .Trim('?', '.', '!', ':', ';', '"', '\'');

        if (requested.StartsWith("access to ", StringComparison.OrdinalIgnoreCase))
        {
            requested = requested["access to ".Length..].Trim();
        }

        if (requested.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
        {
            requested = requested[2..].Trim();
        }

        if (requested.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
        {
            requested = requested[3..].Trim();
        }

        return string.IsNullOrWhiteSpace(requested) ? null : requested;
    }
}
