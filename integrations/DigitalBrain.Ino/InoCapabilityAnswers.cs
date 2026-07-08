namespace DigitalBrain.Ino;

public static class InoCapabilityAnswers
{
    public static bool IsCapabilityQuestion(string prompt)
    {
        // Minimal canonical phrases only (deleted long ad-hoc list per review).
        // Structured matching on records happens inside TryCreateAnswer.
        var p = prompt.ToLowerInvariant();
        return p.Contains("what can you do") ||
               p.Contains("capabilities") ||
               p.Contains("list capabilities") ||
               p.Contains("do you have ");
    }

public static bool TryCreateAnswer(
        string prompt,
        IReadOnlyList<InoCapabilityRecord> agentRecords,
        IReadOnlyList<InoIntentClassifier.Capability> projectedCapabilities,
        out string answer)
    {
        answer = string.Empty;

        // Structured first: scan records for id/alias/display match. If found, answer as specific cap question
        // (even if prompt doesn't match old phrase list). This is the key structured replacement.
        var requested = TryExtractRequestedCapability(prompt, agentRecords, projectedCapabilities);
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

        if (!IsCapabilityQuestion(prompt))
        {
            return false;
        }

        // Inventory (uses minimal phrases)
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

    private static string? TryExtractRequestedCapability(string prompt, IReadOnlyList<InoCapabilityRecord> agentRecords, IReadOnlyList<InoIntentClassifier.Capability> projectedCapabilities)
    {
        var lower = prompt.ToLowerInvariant();

        // Only treat as specific cap question if context suggests "about the capability" (have/available/support/question).
        // This prevents action prompts like "get my last gmail" or "show emails" from being hijacked as cap inventory.
        bool looksLikeCapQuestion = lower.Contains("do you have") ||
                                    lower.Contains("have ") ||
                                    lower.Contains("available") ||
                                    lower.Contains("support") ||
                                    lower.Contains("what can") ||
                                    lower.Contains("list cap") ||
                                    IsCapabilityQuestion(prompt);
        if (!looksLikeCapQuestion)
        {
            return null;
        }

        // Structured: scan for registered ids/aliases/display from records.
        foreach (var r in agentRecords)
        {
            if (lower.Contains(r.Id.ToLowerInvariant()) ||
                lower.Contains(r.DisplayName.ToLowerInvariant()) ||
                r.Aliases.Any(a => !string.IsNullOrWhiteSpace(a) && lower.Contains(a.ToLowerInvariant())))
            {
                return r.Id;
            }
        }
        foreach (var c in projectedCapabilities)
        {
            if (lower.Contains(c.Id.ToLowerInvariant()) ||
                c.Examples.Any(e => lower.Contains(e.ToLowerInvariant())))
            {
                return c.Id;
            }
        }

        // Minimal fallback for "do you have X".
        var marker = lower.IndexOf("do you have", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var requested = prompt[(marker + "do you have".Length)..]
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
