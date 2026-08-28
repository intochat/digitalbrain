using Reqnroll;

namespace DigitalBrain.SmartPrompt;

[Binding]
internal static class BuiltInBehaviorSteps
{
    [Given("X\\.Account\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select the shared X account source.", "X.Account(\"account\")")]
    public static Task XAccount(string account) => Completed(account);

    [When("a new X\\.Post is published")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger when the selected X account publishes a post.", "a new X.Post is published")]
    public static Task NewXPost() => Task.CompletedTask;

    [When("the post mentions \"([^\"]+)\"")]
    [BehaviorStep(BehaviorStepRole.Filter, "Require the X post to contain text.", "the post mentions \"text\"")]
    public static Task PostMentions(string text) => Completed(text);

    [Then("analyze the event as \"([^\"]+)\" with Gemma")]
    [BehaviorStep(BehaviorStepRole.Action, "Analyze event context with local Gemma.", "analyze the event as \"purpose\" with Gemma")]
    public static Task AnalyzeWithGemma(string purpose) => Completed(purpose);

    [Then("add UI\\.Chart\\.Point to UI\\.Chart\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Action, "Append an idempotent linked point to an owner chart.", "add UI.Chart.Point to UI.Chart(\"chart_name\")")]
    public static Task AddChartPoint(string chart) => Completed(chart);

    [Given("fake X\\.Post from \"([^\"]+)\" with text \"([^\"]+)\" and value ([0-9]+(?:\\.[0-9]+)?)")]
    [BehaviorStep(BehaviorStepRole.Fake, "Create a deterministic fake X post.", "fake X.Post from \"account\" with text \"text\" and value 1")]
    public static Task FakeXPost(string account, string text, string value) => Completed(account, text, value);

    [When("behavior \"([^\"]+)\" runs")]
    [BehaviorStep(BehaviorStepRole.Invoke, "Run an installed behavior with the current fake event.", "behavior \"scenario name\" runs")]
    public static Task RunBehavior(string name) => Completed(name);

    [Then("UI\\.Chart\\(\"([^\"]+)\"\\) has point ([0-9]+(?:\\.[0-9]+)?) linking to the source")]
    [BehaviorStep(BehaviorStepRole.Assert, "Assert a linked chart point was written.", "UI.Chart(\"chart_name\") has point 1 linking to the source")]
    public static Task AssertChartPoint(string chart, string value) => Completed(chart, value);

    [Given("Email\\.Account\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select an email account.", "Email.Account(\"account\")")]
    public static Task EmailAccount(string account) => Completed(account);

    [When("a new Email is received")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger for a newly received email.", "a new Email is received")]
    public static Task NewEmail() => Task.CompletedTask;

    [Given("Calendar\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select a calendar.", "Calendar(\"calendar\")")]
    public static Task Calendar(string calendar) => Completed(calendar);

    [When("Calendar\\.Event starts")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger when a calendar event starts.", "Calendar.Event starts")]
    public static Task CalendarEventStarts() => Task.CompletedTask;

    [Given("Market\\.Symbol\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select a market symbol.", "Market.Symbol(\"symbol\")")]
    public static Task MarketSymbol(string symbol) => Completed(symbol);

    [When("Market\\.Price changes")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger when the selected market price changes.", "Market.Price changes")]
    public static Task MarketPriceChanges() => Task.CompletedTask;

    [Given("Folder\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select a watched folder.", "Folder(\"folder\")")]
    public static Task Folder(string folder) => Completed(folder);

    [When("File\\.Created")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger when a file is created.", "File.Created")]
    public static Task FileCreated() => Task.CompletedTask;

    [Given("Health\\.Metric\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select a health metric.", "Health.Metric(\"metric\")")]
    public static Task HealthMetric(string metric) => Completed(metric);

    [When("Health\\.Metric is recorded")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger when a health metric is recorded.", "Health.Metric is recorded")]
    public static Task HealthMetricRecorded() => Task.CompletedTask;

    [Given("GitHub\\.Repository\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select a GitHub repository.", "GitHub.Repository(\"owner/repository\")")]
    public static Task GitHubRepository(string repository) => Completed(repository);

    [When("GitHub\\.Issue is opened")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger when a GitHub issue is opened.", "GitHub.Issue is opened")]
    public static Task GitHubIssueOpened() => Task.CompletedTask;

    [Given("Geofence\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Setup, "Select a location geofence.", "Geofence(\"place\")")]
    public static Task Geofence(string place) => Completed(place);

    [When("Location enters the geofence")]
    [BehaviorStep(BehaviorStepRole.Trigger, "Trigger when the owner enters the geofence.", "Location enters the geofence")]
    public static Task LocationEntered() => Task.CompletedTask;

    [When("the event text contains \"([^\"]+)\"")]
    [BehaviorStep(BehaviorStepRole.Filter, "Require event text to contain a phrase.", "the event text contains \"phrase\"")]
    public static Task EventTextContains(string text) => Completed(text);

    [When("the event value is above ([0-9]+(?:\\.[0-9]+)?)")]
    [BehaviorStep(BehaviorStepRole.Filter, "Require the numeric event value to exceed a threshold.", "the event value is above 1")]
    public static Task EventValueAbove(string value) => Completed(value);

    [Then("notify UI\\.Chat\\(\"([^\"]+)\"\\)")]
    [BehaviorStep(BehaviorStepRole.Action, "Post the behavior result to a chat.", "notify UI.Chat(\"main\")")]
    public static Task NotifyChat(string chat) => Completed(chat);

    [Then("research the sender company with Web\\.Agent")]
    [BehaviorStep(BehaviorStepRole.Action, "Research the sender's company with the web-search agent.", "research the sender company with Web.Agent")]
    public static Task ResearchSenderCompany() => Task.CompletedTask;

    [Then("enrich Salesforce\\.Account with verified company research through MCP")]
    [BehaviorStep(BehaviorStepRole.Action, "Query and update a Salesforce account through its generic MCP tool catalog.", "enrich Salesforce.Account with verified company research through MCP")]
    public static Task EnrichSalesforceAccount() => Task.CompletedTask;

    [Then("preserve verified Salesforce fields")]
    [BehaviorStep(BehaviorStepRole.Action, "Keep Salesforce values that are marked as verified.", "preserve verified Salesforce fields")]
    public static Task PreserveVerifiedSalesforceFields() => Task.CompletedTask;

    [Given("fake event \"([^\"]+)\" from \"([^\"]+)\" with text \"([^\"]+)\" and value ([0-9]+(?:\\.[0-9]+)?)")]
    [BehaviorStep(BehaviorStepRole.Fake, "Create a deterministic provider-neutral fake event.", "fake event \"kind\" from \"source\" with text \"text\" and value 1")]
    public static Task FakeEvent(string kind, string source, string text, string value) => Completed(kind, source, text, value);

    [Then("UI\\.Chat\\(\"([^\"]+)\"\\) contains a behavior notification")]
    [BehaviorStep(BehaviorStepRole.Assert, "Assert that the behavior notified a chat.", "UI.Chat(\"main\") contains a behavior notification")]
    public static Task AssertChatNotification(string chat) => Completed(chat);

    [Then("Salesforce\\.Account preserves its verified Description")]
    [BehaviorStep(BehaviorStepRole.Assert, "Assert enrichment preserves a verified Salesforce description.", "Salesforce.Account preserves its verified Description")]
    public static Task AssertVerifiedSalesforceDescriptionPreserved() => Task.CompletedTask;

    private static Task Completed(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Task.CompletedTask;
    }
}
