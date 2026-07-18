namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Per-neuron capture of the last LLM invocation, so the inspector drawer's
/// Reasoning panel can surface <c>mocked via BDD · {scenario}</c> without
/// having to re-drive the LLM. <see cref="BddMockChatClient"/> writes to the
/// probe on every successful match; Cortex/FirePort code can also write when
/// it attributes a scenario to a downstream neuron. The gateway reads back
/// via <see cref="TryGet"/>.
/// </summary>
public interface IReasoningProbe
{
    void Record(string neuronId, ReasoningRecord record);
    bool TryGet(string neuronId, out ReasoningRecord record);
    IReadOnlyList<string> KnownNeurons();
}

/// <summary>
/// One reasoning hit. <see cref="Source"/> is typically the LLM provider name
/// (<c>bdd-mock</c>, later <c>azure-openai</c>, <c>anthropic</c>);
/// <see cref="ScenarioName"/> names the matched BDD scenario for the mock
/// provider and is empty for real-LLM runs.
/// </summary>
public sealed record ReasoningRecord(
    string Source,
    string ScenarioName,
    string FeatureTitle,
    string Prompt,
    string Reply,
    DateTimeOffset Timestamp);
