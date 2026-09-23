namespace DigitalBrain.Flutter.Workspace;

// Shown once, on a workspace that has never held a window: the assistant plus starter prompts that
// match the workspace's connected sources. It disappears as soon as any window operation is logged.
[GenerateSerializer, Alias("ui.first-run-state")]
public sealed record FirstRunState(
    [property: Id(0)] string AssistantId,
    [property: Id(1)] string AssistantTitle,
    [property: Id(2)] IReadOnlyList<StarterPrompt> Prompts);

[GenerateSerializer, Alias("ui.starter-prompt")]
public sealed record StarterPrompt(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] string Prompt,
    [property: Id(3)] string Source);

// The first-run prompts the workspace offers. The assistant starter is always present; one prompt
// is added per connected source from the declared catalog, so the offer matches what the workspace
// can actually reach.
public static class WorkspaceStarterCatalog
{
    public const string AssistantId = "intocaht";
    public const string AssistantTitle = "IntoChat";

    private static readonly StarterPrompt Assistant = new(
        "assistant",
        "Ask the assistant",
        "Ask the assistant to help with what you are working on.",
        "assistant");

    private static readonly (string Source, StarterPrompt Prompt)[] BySource =
    [
        ("salesforce", new("salesforce", "Review my Salesforce leads", "Review my Salesforce leads and suggest the next action for each.", "salesforce")),
        ("gmail", new("gmail", "Summarize my inbox", "Summarize what needs a reply in my inbox today.", "gmail")),
        ("clickhouse", new("clickhouse", "Ask about my tables", "Answer a question from my connected tables.", "clickhouse")),
    ];

    public static FirstRunState Build(IReadOnlyList<string> connectedSources)
    {
        var prompts = new List<StarterPrompt> { Assistant };
        foreach (var (source, prompt) in BySource)
        {
            if (connectedSources.Contains(source, StringComparer.OrdinalIgnoreCase)) { prompts.Add(prompt); }
        }
        return new FirstRunState(AssistantId, AssistantTitle, prompts);
    }
}
