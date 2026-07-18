namespace Ino.Testing;

/// <summary>
/// Thrown when RecordedMockChatClient cannot find a recording that matches the prompt.
/// The exception message includes the unmatched prompt fragment and a suggested regex
/// to add to the recordings YAML file. Tests should treat this as a test failure — the
/// author either needs to record a new mock or their code is calling the LLM in an
/// unexpected way.
/// </summary>
public sealed class MockLlmMissException : Exception
{
    public MockLlmMissException(string message, string unmatchedPrompt)
        : base(message)
    {
        UnmatchedPrompt = unmatchedPrompt;
    }

    public string UnmatchedPrompt { get; }
}
