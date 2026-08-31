namespace DigitalBrain.SmartPrompt;

public sealed record BehaviorExample(string Name, string Title, string Source, string FakeEventKind);

public static class BehaviorExamples
{
    public static IReadOnlyList<BehaviorExample> All { get; } =
    [
        new("bitcoin-tracker", "Track Elon Bitcoin posts", BitcoinTracker, "x.post"),
        new("urgent-email", "Explain urgent email", UrgentEmail, "email.received"),
        new("travel-calendar", "Prepare for travel events", TravelCalendar, "calendar.event"),
        new("portfolio-threshold", "Track market thresholds", PortfolioThreshold, "market.price"),
        new("file-summarizer", "Summarize new files", FileSummarizer, "file.created"),
        new("health-anomaly", "Explain health anomalies", HealthAnomaly, "health.metric"),
        new("github-triage", "Triage GitHub issues", GitHubTriage, "github.issue"),
        new("arrival-reminder", "Remind on arrival", ArrivalReminder, "location.entered"),
        new("salesforce-account-enrichment", "Enrich Salesforce accounts from company email", SalesforceAccountEnrichment, "email.received"),
    ];

    public static BehaviorExample? Find(string name)
        => All.FirstOrDefault(example => string.Equals(example.Name, name, StringComparison.Ordinal));

    private const string BitcoinTracker =
        """
        Feature: Bitcoin tracker

          @behavior
          Scenario: Track Elon posts about Bitcoin
            Given X.Account("elonmusk")
            When a new X.Post is published
            And the post mentions "bitcoin"
            Then analyze the event as "bitcoin market signal" with AI
            And add UI.Chart.Point to UI.Chart("bitcoin_tracker")

          @test
          Scenario: An Elon Bitcoin post adds a linked point
            Given fake X.Post from "elonmusk" with text "Bitcoin reaches 95000" and value 95000
            When behavior "Track Elon posts about Bitcoin" runs
            Then UI.Chart("bitcoin_tracker") has point 95000 linking to the source
        """;

    private const string UrgentEmail =
        """
        Feature: Urgent email explainer
          @behavior
          Scenario: Explain urgent work email
            Given Email.Account("work")
            When a new Email is received
            And the event text contains "urgent"
            Then analyze the event as "urgency and next action" with AI
            And notify UI.Chat("main")
          @test
          Scenario: Urgent mail creates a notification
            Given fake event "email.received" from "work" with text "urgent contract review" and value 1
            When behavior "Explain urgent work email" runs
            Then UI.Chat("main") contains a behavior notification
        """;

    private const string TravelCalendar =
        """
        Feature: Travel preparation
          @behavior
          Scenario: Prepare for a travel event
            Given Calendar("primary")
            When Calendar.Event starts
            And the event text contains "flight"
            Then analyze the event as "travel preparation checklist" with AI
            And notify UI.Chat("main")
          @test
          Scenario: Flight event creates a checklist
            Given fake event "calendar.event" from "primary" with text "flight to Prague" and value 1
            When behavior "Prepare for a travel event" runs
            Then UI.Chat("main") contains a behavior notification
        """;

    private const string PortfolioThreshold =
        """
        Feature: Portfolio threshold
          @behavior
          Scenario: Track BTC above threshold
            Given Market.Symbol("BTCUSD")
            When Market.Price changes
            And the event value is above 90000
            Then add UI.Chart.Point to UI.Chart("portfolio")
          @test
          Scenario: High price updates portfolio
            Given fake event "market.price" from "BTCUSD" with text "BTC breakout" and value 95000
            When behavior "Track BTC above threshold" runs
            Then UI.Chart("portfolio") has point 95000 linking to the source
        """;

    private const string FileSummarizer =
        """
        Feature: File summarizer
          @behavior
          Scenario: Summarize an incoming document
            Given Folder("inbox")
            When File.Created
            Then analyze the event as "document summary" with AI
            And notify UI.Chat("main")
          @test
          Scenario: New document posts a summary
            Given fake event "file.created" from "inbox" with text "quarterly plan" and value 1
            When behavior "Summarize an incoming document" runs
            Then UI.Chat("main") contains a behavior notification
        """;

    private const string HealthAnomaly =
        """
        Feature: Health anomaly
          @behavior
          Scenario: Explain high heart rate
            Given Health.Metric("heart_rate")
            When Health.Metric is recorded
            And the event value is above 120
            Then analyze the event as "health metric context, not medical advice" with AI
            And add UI.Chart.Point to UI.Chart("health")
          @test
          Scenario: High heart rate updates health chart
            Given fake event "health.metric" from "heart_rate" with text "after a run" and value 135
            When behavior "Explain high heart rate" runs
            Then UI.Chart("health") has point 135 linking to the source
        """;

    private const string GitHubTriage =
        """
        Feature: GitHub issue triage
          @behavior
          Scenario: Triage a new issue
            Given GitHub.Repository("digitalbrain")
            When GitHub.Issue is opened
            Then analyze the event as "severity, component, and next action" with AI
            And notify UI.Chat("main")
          @test
          Scenario: New issue posts triage
            Given fake event "github.issue" from "digitalbrain" with text "crash on startup" and value 1
            When behavior "Triage a new issue" runs
            Then UI.Chat("main") contains a behavior notification
        """;

    private const string ArrivalReminder =
        """
        Feature: Arrival reminder
          @behavior
          Scenario: Remind me when I arrive home
            Given Geofence("home")
            When Location enters the geofence
            Then analyze the event as "short context-aware reminder" with AI
            And notify UI.Chat("main")
          @test
          Scenario: Arrival posts the reminder
            Given fake event "location.entered" from "home" with text "pick up the parcel" and value 1
            When behavior "Remind me when I arrive home" runs
            Then UI.Chat("main") contains a behavior notification
        """;

    private const string SalesforceAccountEnrichment =
        """
        Feature: Salesforce account enrichment
          @behavior
          Scenario: Enrich Salesforce account from a new company email
            Given Email.Account("vlad@intochat.io")
            When a new Email is received
            Then research the sender company with Web.Agent
            And enrich Salesforce.Account with verified company research through MCP
            And notify UI.Chat("main")
          @test
          Scenario: IntoChat email enriches its Salesforce account
            Given fake event "email.received" from "vlad@intochat.io" with text "new company email from IntoChat" and value 1
            When behavior "Enrich Salesforce account from a new company email" runs
            Then UI.Chat("main") contains a behavior notification
        """;
}
