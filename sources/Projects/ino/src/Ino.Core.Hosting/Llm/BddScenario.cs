namespace Ino.Core.Hosting.Llm;

/// <summary>
/// One Gherkin scenario resolved into an LLM prompt/response pair. The
/// <see cref="BddMockChatClient"/> regex-matches <see cref="PromptPattern"/>
/// against the last user message in a chat request and returns
/// <see cref="ReplyText"/> when it hits. The other fields power the inspector
/// Reasoning panel — a user tapping FlightSearch sees
/// <c>mocked via BDD · {FeatureTitle} — {ScenarioName}</c>.
/// </summary>
public sealed record BddScenario(
    string FeatureTitle,
    string ScenarioName,
    string PromptPattern,
    string ReplyText,
    IReadOnlyList<string> Tags,
    string SourceFile);
