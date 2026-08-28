using System.Globalization;

namespace DigitalBrain.SmartPrompt;

public static class FakeBehaviorEvents
{
    public static BehaviorEvent Create(string behaviorName, string? eventId = null)
    {
        var id = eventId ?? $"fake-{behaviorName}-{Guid.NewGuid():N}";
        var at = DateTimeOffset.UtcNow;
        return behaviorName switch
        {
            "bitcoin-tracker" => Event(id, "x.post", "elonmusk", "Bitcoin reaches 95000", 95000,
                "https://x.com/elonmusk/status/1900000000000000000", at),
            "urgent-email" => Event(id, "email.received", "work", "urgent contract review", 1,
                "digitalbrain://email/work/fake-urgent", at),
            "travel-calendar" => Event(id, "calendar.event", "primary", "flight to Prague", 1,
                "digitalbrain://calendar/primary/fake-flight", at),
            "portfolio-threshold" => Event(id, "market.price", "BTCUSD", "BTC breakout", 95000,
                "https://example.test/markets/BTCUSD", at),
            "file-summarizer" => Event(id, "file.created", "inbox", "quarterly plan", 1,
                "digitalbrain://files/inbox/quarterly-plan", at),
            "health-anomaly" => Event(id, "health.metric", "heart_rate", "after a run", 135,
                "digitalbrain://health/heart-rate/fake", at),
            "github-triage" => Event(id, "github.issue", "digitalbrain", "crash on startup", 1,
                "https://github.com/example/digitalbrain/issues/42", at),
            "arrival-reminder" => Event(id, "location.entered", "home", "pick up the parcel", 1,
                "digitalbrain://location/home/fake", at),
            "salesforce-account-enrichment" => Event(id, "email.received", "vlad@intochat.io",
                "new company email from IntoChat", 1, "digitalbrain://gmail/thread-intochat", at),
            _ => throw new ArgumentException($"Unknown built-in behavior '{behaviorName}'.", nameof(behaviorName)),
        };
    }

    private static BehaviorEvent Event(
        string id, string kind, string source, string text, double value, string uri, DateTimeOffset at)
        => new(id, kind, source, text, value, uri, at);

    public static string Describe(BehaviorEvent behaviorEvent)
        => $"{behaviorEvent.Kind} from {behaviorEvent.Source}: {behaviorEvent.Text} "
           + $"({behaviorEvent.Value.ToString(CultureInfo.InvariantCulture)})";
}
