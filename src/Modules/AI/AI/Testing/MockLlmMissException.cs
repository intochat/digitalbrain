namespace DigitalBrain.AI;

// Thrown when the corpus-scripted mock LLM receives a prompt no scenario claims.
// Loud on purpose: a miss means the test forgot a scenario or a Given regex drifted,
// and the fix is in the corpus, never in retry logic.
public sealed class MockLlmMissException : Exception
{
    public MockLlmMissException()
    {
    }

    public MockLlmMissException(string message)
        : base(message)
    {
    }

    public MockLlmMissException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static MockLlmMissException ForPrompt(string prompt, IReadOnlyList<string> givenPatterns)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(givenPatterns);

        var loaded = givenPatterns.Count == 0
            ? "  (none — the corpus loaded no scenarios)"
            : string.Join(Environment.NewLine, givenPatterns.Select(static pattern => $"  {pattern}"));

        return new MockLlmMissException(
            $"No BDD scenario matches the user prompt.{Environment.NewLine}"
            + $"Prompt: {prompt}{Environment.NewLine}"
            + $"Loaded Given patterns:{Environment.NewLine}{loaded}");
    }
}
