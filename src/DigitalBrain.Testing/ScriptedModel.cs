using System.Collections.Concurrent;

namespace DigitalBrain.Testing;

[GenerateSerializer]
[Alias("db.testing.unscripted")]
public sealed class UnscriptedPromptException : Exception
{
    public UnscriptedPromptException()
        : this("The scripted model has no answer for that prompt.")
    {
    }

    public UnscriptedPromptException(string message)
        : base(message)
    {
    }

    public UnscriptedPromptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ScriptedModel
{
    private readonly ConcurrentDictionary<string, string> _answers = new(StringComparer.OrdinalIgnoreCase);

    public void Answer(string prompt, string answer) => _answers[prompt] = answer;

    public void Forget() => _answers.Clear();

    public string Complete(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return _answers.TryGetValue(prompt, out var answer)
            ? answer
            : throw new UnscriptedPromptException(
                $"The scripted model has no answer for \"{prompt}\". Script it with a \"the <tier> model answers\" step instead of letting a scenario invent one.");
    }
}
