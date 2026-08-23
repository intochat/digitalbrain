namespace DigitalBrain.SmartPrompt;

internal static class DefaultSmartPromptCatalog
{
    public const string NewCustomersName = "new-customers";

    public static SmartPromptDocument NewCustomers { get; } = new(
        Title: "New Customer intake",
        BodyText:
            "Every 15 mins check whether there are new emails to Gmail with topic New Customer, "
            + "make a websearch on that company and populate data to Salesforce, "
            + "then display a chart with new customers for the last week.",
        Bindings:
        [
            new SmartPromptBinding("gmail", "vlad@digitalbrain.com", "vlad@digitalbrain.com"),
            new SmartPromptBinding("websearch", "company research", null),
            new SmartPromptBinding("salesforce", "upsert lead", null),
            new SmartPromptBinding("chart", "new customers last week", null),
            new SmartPromptBinding("schedule", "15m", null),
        ],
        Enabled: true);

    public static IReadOnlyList<string> Names { get; } = [NewCustomersName];
}
