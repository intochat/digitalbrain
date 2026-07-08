using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Ino;

public static class InoIntentClassifier
{
    public sealed record Capability(string Id, string Description, string[] Examples, string Tier = "generic");

    public sealed record Classification(string Intent, double Confidence, string? Query = null, int? MaxResults = null);

    private const int MaxKeywordCapabilities = 5;

    public static Classification Classify(string prompt, IReadOnlyList<InoCapabilityRecord>? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new("generic", 0.0);
        }

        capabilities ??= InoAgentCapabilities.DiscoverAgentRecords()
            .Concat(InoIntentHandlers.CapabilityRecords)
            .ToArray();

        if (InoExplanationFormatter.TryParse(prompt).Kind != InoExplanationRequestKind.None)
        {
            return new("explain", 0.95);
        }

        if (InoCapabilityAnswers.TryParseQuery(prompt, capabilities).Kind != InoCapabilityQueryKind.None)
        {
            return new("capability_status", 0.95);
        }

        var max = InoPromptSemantics.ResultCount(prompt);
        var query = InoPromptSemantics.TryExtractFromQuery(prompt);

        if (InoPromptSemantics.MatchesCapability(prompt, capabilities, "relation_graph"))
        {
            return new("relation_graph", 0.8);
        }

        if (InoPromptSemantics.MatchesCapability(prompt, capabilities, "schema_viz"))
        {
            return new("schema_viz", 0.8);
        }

        if (InoPromptSemantics.MatchesCapability(prompt, capabilities, "uikit_gallery"))
        {
            return new("uikit_gallery", 0.9);
        }

        if (InoPromptSemantics.HasAny(prompt, "set-llm"))
        {
            return new("set_llm", 0.95);
        }

        if (InoPromptSemantics.MatchesCapability(prompt, capabilities, "llm_settings"))
        {
            return new("llm_settings", 0.85);
        }

        if (InoPromptSemantics.HasAny(prompt, "automation") ||
            InoPromptSemantics.HasAll(prompt, "create", "reaction") ||
            InoPromptSemantics.HasAll(prompt, "when", "then"))
        {
            return new("automation_create", 0.8);
        }

        if (InoPromptSemantics.HasAll(prompt, "run", "automation") ||
            InoPromptSemantics.HasAll(prompt, "execute", "automation"))
        {
            return new("run_automation", 0.85);
        }

        if (InoPromptSemantics.HasAny(prompt, "approve") &&
            InoPromptSemantics.HasAny(prompt, "proposal", "automation", "self-evolution"))
        {
            return new("approve", 0.9);
        }

        if (capabilities != null)
        {
            var matchedCap = capabilities.FirstOrDefault(c => InoPromptSemantics.MatchesCapability(prompt, capabilities, c.Id));
            if (matchedCap != null)
            {
                return new(matchedCap.Id, 0.82, query, max);
            }
        }

        return new("generic", 0.3);
    }

    public static async Task<Classification> ClassifyWithLlmAsync(
        string prompt,
        IServiceProvider? services = null,
        IReadOnlyList<InoCapabilityRecord>? capabilities = null,
        CancellationToken cancellationToken = default)
    {
        capabilities ??= InoAgentCapabilities.DiscoverAgentRecords()
            .Concat(InoIntentHandlers.CapabilityRecords)
            .ToArray();
        var fast = Classify(prompt, capabilities);
        if (fast.Confidence >= 0.8)
        {
            return fast;
        }

        var chat = services?.GetService<IChatClient>();
        if (chat is null)
        {
            return fast with { Confidence = Math.Max(fast.Confidence, 0.4) };
        }

        try
        {
            var relevant = await RetrieveCapabilitiesAsync(prompt, capabilities, services, cancellationToken);
            var capsText = string.Join("\n", relevant.Select(c => $"- {c.Id}: {c.Description} (e.g. {string.Join(", ", c.Examples)})"));

            const string sys = "You are an intent classifier for a personal AI assistant. " +
                               "Reply with ONLY a single JSON object: {\"intent\":\"generic\",\"confidence\":0.7}. " +
                               "Use only listed capability ids or generic/explain/approve/run_automation/uikit_gallery/schema_viz/relation_graph:\n";

            var fullPrompt = sys + capsText + "\nUser request: " + SecretText.Redact(prompt);
            var response = await chat.GetResponseAsync(fullPrompt, cancellationToken: cancellationToken);

            var text = response.Text?.Trim() ?? "";
            var parsed = TryParseClassification(text, capabilities);
            if (parsed is not null && parsed.Confidence > 0.5)
            {
                return parsed;
            }

            return fast;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return fast;
        }
    }

    public static List<InoCapabilityRecord> RetrieveCapabilities(string prompt, IReadOnlyList<InoCapabilityRecord> caps) =>
        KeywordCapabilities(prompt, caps);

    public static async Task<List<InoCapabilityRecord>> RetrieveCapabilitiesAsync(string prompt, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        var caps = InoAgentCapabilities.DiscoverAgentRecords()
            .Concat(InoIntentHandlers.CapabilityRecords)
            .ToArray();
        return await RetrieveCapabilitiesAsync(prompt, caps, services, cancellationToken);
    }

    public static async Task<List<InoCapabilityRecord>> RetrieveCapabilitiesAsync(
        string prompt,
        IReadOnlyList<InoCapabilityRecord> caps,
        IServiceProvider? services = null,
        CancellationToken cancellationToken = default)
    {
        caps ??= [];
        var keyword = KeywordCapabilities(prompt, caps);

        var vectorCaps = new List<InoCapabilityRecord>();
        if (services != null)
        {
            try
            {
                var recall = services.GetService<IInoCapabilityRecall>();
                if (recall != null)
                {
                    var recalled = await recall.RecallAsync(prompt, top: 5, cancellationToken);
                    foreach (var text in recalled ?? Array.Empty<string>())
                    {
                        // Parse structured "capability:Id source:..." from memory text without regex
                        var capPrefix = "capability:";
                        var idx = text.IndexOf(capPrefix, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            var start = idx + capPrefix.Length;
                            var end = text.IndexOf(' ', start);
                            if (end < 0) end = text.Length;
                            var id = text.Substring(start, end - start).Trim();
                            var cap = caps.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                            if (cap != null && !vectorCaps.Any(c => c.Id == cap.Id))
                            {
                                vectorCaps.Add(cap);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        var combined = keyword
            .Concat(vectorCaps)
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(MaxKeywordCapabilities)
            .ToList();

        return combined;
    }

    private static List<InoCapabilityRecord> KeywordCapabilities(string prompt, IReadOnlyList<InoCapabilityRecord> caps)
    {
        return caps
            .Where(c => c.Matches(prompt))
            .OrderByDescending(c => InoPromptSemantics.HasAny(prompt, c.Id) ? 2 : 1)
            .Take(MaxKeywordCapabilities)
            .ToList();
    }

    private static Classification? TryParseClassification(string text, IReadOnlyList<InoCapabilityRecord> capabilities)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("intent", out var intentElement) ||
                intentElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var intent = intentElement.GetString() ?? "generic";
            if (!IsAllowedIntent(intent, capabilities))
            {
                return null;
            }

            var confidence = root.TryGetProperty("confidence", out var confidenceElement) &&
                             confidenceElement.TryGetDouble(out var parsedConfidence)
                ? parsedConfidence
                : 0.65;

            return new Classification(intent, Math.Clamp(confidence, 0.0, 1.0));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAllowedIntent(string intent, IReadOnlyList<InoCapabilityRecord> capabilities) =>
        string.Equals(intent, "generic", StringComparison.Ordinal) ||
        string.Equals(intent, "explain", StringComparison.Ordinal) ||
        string.Equals(intent, "approve", StringComparison.Ordinal) ||
        string.Equals(intent, "run_automation", StringComparison.Ordinal) ||
        string.Equals(intent, "uikit_gallery", StringComparison.Ordinal) ||
        capabilities.Any(capability =>
            string.Equals(capability.Id, intent, StringComparison.OrdinalIgnoreCase) ||
            capability.Matches(intent));
}
