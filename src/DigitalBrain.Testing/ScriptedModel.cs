using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

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

public sealed class ScriptedModel : IChatClient
{
    private static readonly ChatClientMetadata Description = new(providerName: "digitalbrain.scripted");

    private readonly ConcurrentDictionary<string, string> _answers = new(StringComparer.OrdinalIgnoreCase);

    public void Answer(string prompt, string answer) => _answers[prompt] = answer;

    public void Forget() => _answers.Clear();

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, AnswerTo(messages))));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        yield return new ChatResponseUpdate(ChatRole.Assistant, AnswerTo(messages));

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is not null ? null
            : serviceType == typeof(ChatClientMetadata) ? Description
            : serviceType.IsInstanceOfType(this) ? this
            : null;
    }

    public void Dispose() => _answers.Clear();

    private string AnswerTo(IEnumerable<ChatMessage> messages)
    {
        var prompt = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;

        return _answers.TryGetValue(prompt, out var answer)
            ? answer
            : throw new UnscriptedPromptException(
                $"The scripted model has no answer for \"{prompt}\". Script it with a \"the <tier> model answers\" step instead of letting a scenario invent one.");
    }
}
