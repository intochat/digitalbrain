using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Ino;

/// Lightweight intent classifier for Ino.
/// Starts with fast keyword rules (replacing brittle Regex) + optional LLM structured classification
/// for natural language robustness (Gmail, Salesforce, etc.).
/// Capabilities registry (stub for vector index): descriptions/examples for grounding LLM classify.
/// Future: register on pack/automation apply, embed via Context/Qdrant, retrieve top-k.
public static class InoIntentClassifier
{
    public sealed record Capability(string Id, string Description, string[] Examples, string Tier = "generic");

    private static readonly List<Capability> _caps = new()
    {
        new Capability("gmail", "Read or act on Gmail messages", new[] { "show my emails", "last gmail from boss" }, "gmail"),
        new Capability("salesforce", "Query Salesforce CRM accounts/contacts", new[] { "list salesforce accounts", "salesforce from Acme" }, "salesforce"),
        new Capability("automation_create", "Create a new reaction/automation", new[] { "when gmail then summarize", "if email then note in crm" }, "automation"),
        new Capability("llm_settings", "Change or view active LLM/model", new[] { "change llm to gpt", "llm settings" }, "generic"),
        new Capability("uikit_gallery", "Show UI component gallery", new[] { "ui kit gallery", "show components" }, "ui"),
    };

    public static IReadOnlyList<Capability> Capabilities => _caps;

    public static void RegisterCapability(Capability cap)
    {
        if (!_caps.Any(c => c.Id == cap.Id))
            _caps.Add(cap);
    }

    public sealed record Classification(string Intent, double Confidence, string? Query = null, int? MaxResults = null);

    // Fast path used by handlers and legacy Is* helpers. No LLM required.
    public static Classification Classify(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return new("generic", 0.0);

        var p = prompt.ToLowerInvariant();

        int? max = null;
        if (p.Contains("last") || p.Contains("latest") || p.Contains("most recent")) max = 1;
        else if (p.Contains("5") || p.Contains("few")) max = 5;

        string? query = null;
        if (p.Contains(" from ")) 
        {
            // naive extract e.g. "emails from bob"
            var parts = prompt.Split(new[] { " from ", " From " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1) query = parts[1].Trim().Split(' ').FirstOrDefault();
        }

        // Salesforce / CRM (check before gmail to allow cross "salesforce ... last email" follow-ups)
        if (p.Contains("salesforce") || p.Contains("crm"))
            return new("salesforce", 0.88, query, max);

        // Gmail / email family (avoid stealing explicit salesforce cross follow-ups)
        if (p.Contains("gmail") || p.Contains("inbox") || p.Contains("mailbox") ||
            (p.Contains("email") && !p.Contains("salesforce") && !p.Contains("crm")))
            return new("gmail", 0.85, query, max);

        // Existing static intents (kept for compatibility)
        if (p.Contains("bitcoin") && p.Contains("price"))
            return new("bitcoin_price", 0.9);

        if ((p.Contains("draw") || p.Contains("show") || p.Contains("visualize")) &&
            p.Contains("relation") && (p.Contains("2 object") || p.Contains("two object") || p.Contains("object")))
            return new("relation_graph", 0.8);

        if (p.Contains("schema") || p.Contains("visualize database") || p.Contains("visualize db") ||
            p.Contains("show database") || p.Contains("show db"))
            return new("schema_viz", 0.8);

        if (p.Contains("uikit") || p.Contains("ui kit") || (p.Contains("gallery") && p.Contains("component")))
            return new("uikit_gallery", 0.9);

        if (p.Contains("llm") || p.Contains("model") || p.Contains("settings") || 
            p.Contains("change llm") || p.Contains("use qwen") || p.Contains("use gpt") || p.Contains("openai"))
            return new("llm_settings", 0.85);

        if (p.Contains("automation") || p.Contains("create reaction") || (p.Contains("when") && p.Contains("then")) || p.Contains("if ") && p.Contains(" then "))
            return new("automation_create", 0.8);

        return new("generic", 0.3);
    }

    // LLM-enhanced path (async). Falls back to Classify() result if no chat client or low confidence.
    // Callers (handlers) can use this for ambiguous cases or always for "magical" feel.
    public static async Task<Classification> ClassifyWithLlmAsync(string prompt, IServiceProvider? services = null)
    {
        var fast = Classify(prompt);
        if (fast.Confidence >= 0.8)
            return fast; // fast path wins for obvious cases

        var chat = services?.GetService<IChatClient>();
        if (chat is null)
            return fast with { Confidence = Math.Max(fast.Confidence, 0.4) };

        try
        {
            // Retrieval using Context vector (caps remembered as memories via InoNeuron) + keyword fallback.
            // This is the basic vector index for Slice B.
            var relevant = await RetrieveCapabilitiesAsync(prompt, services);
            var capsText = string.Join("\n", relevant.Select(c => $"- {c.Id}: {c.Description} (e.g. {string.Join(", ", c.Examples)})"));

            const string sys = "You are an intent classifier for a personal AI assistant. " +
                               "Reply with ONLY a single JSON object: {\"intent\":\"gmail\",\"confidence\":0.92}. " +
                               "Ground on these capabilities (use best match):\n";

            var fullPrompt = sys + capsText + "\nUser request: " + prompt;
            var response = await chat.GetResponseAsync(fullPrompt);

            var text = response.Text?.Trim() ?? "";
            var parsed = TryParseClassification(text);
            if (parsed is not null && parsed.Confidence > 0.5)
                return parsed;

            return fast;
        }
        catch
        {
            return fast;
        }
    }

    // Sync version for backward compat (keyword only).
    public static List<Capability> RetrieveCapabilities(string prompt) =>
        RetrieveCapabilitiesAsync(prompt, null).GetAwaiter().GetResult();

    public static async Task<List<Capability>> RetrieveCapabilitiesAsync(string prompt, IServiceProvider? services = null)
    {
        var p = prompt.ToLowerInvariant();
        // Keyword fallback (always fast and reliable)
        var keyword = Capabilities
            .Where(c => p.Contains(c.Id) || c.Examples.Any(e => p.Contains(e.ToLowerInvariant())))
            .OrderByDescending(c => p.Contains(c.Id) ? 2 : 1)
            .Take(5)
            .ToList();

        var vectorCaps = new List<Capability>();
        if (services != null)
        {
            try
            {
                // Resolve grain factory to reach ContextNeuron (vector store + hybrid recall)
                // Caps are remembered on Ino activate as "capability:ID ..." texts with embeddings.
                var gf = services.GetService(typeof(Orleans.IGrainFactory)) as Orleans.IGrainFactory;
                if (gf != null)
                {
                    var ctx = gf.GetGrain<DigitalBrain.Context.IContextNeuron>("context-main");
                    // Use the user prompt; embeddings + HybridScorer will find semantically close capability memories.
                    var recalled = await ctx.RecallAsync(prompt, top: 5);
                    foreach (var text in recalled ?? Array.Empty<string>())
                    {
                        // Parse remembered format: "capability:ID description examples:... tier:..."
                        var idMatch = System.Text.RegularExpressions.Regex.Match(text, @"capability:(\S+)");
                        if (idMatch.Success)
                        {
                            var id = idMatch.Groups[1].Value;
                            var cap = Capabilities.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                            if (cap != null && !vectorCaps.Any(c => c.Id == cap.Id))
                                vectorCaps.Add(cap);
                        }
                    }
                }
            }
            catch
            {
                // Context / vector optional; degrade to keyword (as documented in IContextNeuron)
            }
        }

        // Merge keyword + vector results, dedup, take top-k for LLM grounding.
        var combined = keyword
            .Concat(vectorCaps)
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(5)
            .ToList();

        return combined;
    }

    private static Classification? TryParseClassification(string text)
    {
        // Minimal parser (no Regex, no heavy JSON). Looks for known labels and simple numbers.
        try
        {
            var lower = text.ToLowerInvariant();
            string intent = "generic";
            double conf = 0.65;

            if (lower.Contains("gmail") || lower.Contains("email")) intent = "gmail";
            else if (lower.Contains("salesforce") || lower.Contains("crm")) intent = "salesforce";
            else if (lower.Contains("bitcoin")) intent = "bitcoin_price";
            else if (lower.Contains("relation")) intent = "relation_graph";
            else if (lower.Contains("schema")) intent = "schema_viz";
            else if (lower.Contains("llm") || lower.Contains("model") || lower.Contains("settings")) intent = "llm_settings";
            else if (lower.Contains("automation") || lower.Contains("when") && lower.Contains("then")) intent = "automation_create";

            // crude number scan for confidence (e.g. 0.87 or 87%)
            foreach (var token in lower.Split(new[] { ' ', '\n', ',', ':', '"', '{', '}' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("0.") && double.TryParse(token, out var c))
                {
                    conf = c;
                    break;
                }
                if (token.EndsWith("%") && double.TryParse(token.TrimEnd('%'), out var p))
                {
                    conf = p / 100.0;
                    break;
                }
            }

            return new Classification(intent, Math.Clamp(conf, 0.0, 1.0));
        }
        catch
        {
            return null;
        }
    }
}
