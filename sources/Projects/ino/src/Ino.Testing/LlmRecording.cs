namespace Ino.Testing;

/// <summary>
/// One recording from a mocks/llm.recordings.yml file. Phase 1 supports text responses
/// only; Phase 4 extends with tool_calls and structured json responses.
/// </summary>
public sealed class LlmRecording
{
    /// <summary>Regex pattern matched against the last user message in the ChatRequest.</summary>
    public string Match { get; set; } = null!;

    /// <summary>The text content of the mocked response.</summary>
    public string? Text { get; set; }
}
