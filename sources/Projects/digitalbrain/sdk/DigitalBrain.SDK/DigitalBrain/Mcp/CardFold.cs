using System.Text.Json;

namespace DigitalBrain.SDK.DigitalBrain.Mcp;

public static class CardFold
{
    public static bool IsTerminal(CardView card) => card.RootWidget switch
    {
        "EngineeringSummaryCard" => true,
        "ErrorSummaryCard"       => true,
        "CreatorProgressCard"    => StageOf(card.DataJson) == "Promoted",
        "GoogleAuthCard"         => true,
        "DigitalBrain.SDK.Google.OAuthConsentRequired" => true,
        _                        => false,
    };

    public static BrainResult Reduce(IReadOnlyList<CardView> cards, bool timedOut)
    {
        string? feature = null;
        CodeBundle? code = null;
        string? neuronId = null;
        var stages = new List<string>();
        string outcome = timedOut ? "timeout" : "failed";
        string? failure = null;

        foreach (var c in cards)
        {
            switch (c.RootWidget)
            {
                // On retries, feature text is overwritten (latest attempt wins) while neuronId is kept (stable slug).
                case "FeatureCard":
                    feature = Str(c.DataJson, "gherkinText");
                    neuronId ??= Str(c.DataJson, "neuronId");
                    break;
                case "CodeCard":
                    code = new CodeBundle(
                        Str(c.DataJson, "implCode") ?? "",
                        Str(c.DataJson, "stepsCode") ?? "");
                    neuronId ??= Str(c.DataJson, "neuronId");
                    break;
                case "CreatorProgressCard":
                    var st = StageOf(c.DataJson);
                    if (st is not null) stages.Add(st);
                    break;
                case "EngineeringSummaryCard":
                    outcome = Str(c.DataJson, "outcome") is { Length: > 0 } s ? s : outcome;
                    neuronId = Str(c.DataJson, "neuronId") ?? neuronId;
                    break;
                case "GoogleAuthCard":
                case "DigitalBrain.SDK.Google.OAuthConsentRequired":
                    outcome = "consent_required";
                    break;
                case "ErrorSummaryCard":
                    outcome = "failed";
                    failure = Str(c.DataJson, "finalError");
                    neuronId ??= Str(c.DataJson, "neuronId");
                    break;
            }
        }

        var testResult = failure is not null
            ? $"failed: {failure}"
            : (stages.Count > 0 ? string.Join(" -> ", stages) : null);

        return new BrainResult(outcome, neuronId, feature, code, testResult, cards);
    }

    static string? StageOf(string dataJson) => Str(dataJson, "stage");

    static string? Str(string json, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(prop, out var v)
                && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }
}
