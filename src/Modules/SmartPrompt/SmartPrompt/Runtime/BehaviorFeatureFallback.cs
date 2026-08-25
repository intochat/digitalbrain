using System.Globalization;
using System.Text.RegularExpressions;

namespace DigitalBrain.SmartPrompt;

/// <summary>
/// Produces a compiler-safe program when a small local model cannot repair its Gherkin shape.
/// The model is still called first; this fallback constrains its requested intent to the runtime's
/// currently installed trigger and action vocabulary.
/// </summary>
public static partial class BehaviorFeatureFallback
{
    public static string FromRequest(string request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        var example = BestMatch(request);
        var source = example.Source;
        var threshold = Threshold().Match(request);
        if (!threshold.Success || !source.Contains("the event value is above", StringComparison.Ordinal))
        {
            return source;
        }

        var requested = double.Parse(threshold.Groups[1].Value, CultureInfo.InvariantCulture);
        var fake = requested + 1;
        var requestedText = requested.ToString("0.################", CultureInfo.InvariantCulture);
        var fakeText = fake.ToString("0.################", CultureInfo.InvariantCulture);
        source = ExistingThreshold().Replace(source, $"the event value is above {requestedText}", 1);
        source = FakeValue().Replace(source, match => $"{match.Groups[1].Value}{fakeText}", 1);
        return PointValue().Replace(source, match => $"{match.Groups[1].Value}{fakeText}{match.Groups[3].Value}", 1);
    }

    public static BehaviorExample BestMatch(string request)
    {
        var text = request.ToLowerInvariant();
        var name = text.Contains("x post", StringComparison.Ordinal)
            || text.Contains("twitter", StringComparison.Ordinal)
            || text.Contains("tweet", StringComparison.Ordinal)
            || text.Contains("elon", StringComparison.Ordinal)
            ? "bitcoin-tracker"
            : text.Contains("email", StringComparison.Ordinal) || text.Contains("mail", StringComparison.Ordinal)
                ? "urgent-email"
                : text.Contains("calendar", StringComparison.Ordinal) || text.Contains("meeting", StringComparison.Ordinal)
                    || text.Contains("flight", StringComparison.Ordinal) || text.Contains("travel", StringComparison.Ordinal)
                    ? "travel-calendar"
                    : text.Contains("market", StringComparison.Ordinal) || text.Contains("portfolio", StringComparison.Ordinal)
                        || text.Contains("price", StringComparison.Ordinal) || text.Contains("bitcoin", StringComparison.Ordinal)
                        || text.Contains("btc", StringComparison.Ordinal)
                        ? "portfolio-threshold"
                        : text.Contains("file", StringComparison.Ordinal) || text.Contains("folder", StringComparison.Ordinal)
                            || text.Contains("document", StringComparison.Ordinal)
                            ? "file-summarizer"
                            : text.Contains("health", StringComparison.Ordinal) || text.Contains("heart", StringComparison.Ordinal)
                                ? "health-anomaly"
                                : text.Contains("github", StringComparison.Ordinal) || text.Contains("issue", StringComparison.Ordinal)
                                    || text.Contains("repository", StringComparison.Ordinal)
                                    ? "github-triage"
                                    : text.Contains("location", StringComparison.Ordinal) || text.Contains("arrive", StringComparison.Ordinal)
                                        || text.Contains("geofence", StringComparison.Ordinal)
                                        ? "arrival-reminder"
                                        : "urgent-email";
        return BehaviorExamples.Find(name)!;
    }

    [GeneratedRegex(@"(?:above|over|greater than|exceeds?)\s*\$?([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex Threshold();

    [GeneratedRegex(@"the event value is above [0-9]+(?:\.[0-9]+)?")]
    private static partial Regex ExistingThreshold();

    [GeneratedRegex(@"(\bvalue )[0-9]+(?:\.[0-9]+)?")]
    private static partial Regex FakeValue();

    [GeneratedRegex(@"(has point )([0-9]+(?:\.[0-9]+)?)( linking to the source)")]
    private static partial Regex PointValue();
}
