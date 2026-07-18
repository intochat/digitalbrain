using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Brain.Tests.AI;

public sealed class ScriptedChatClient : IChatClient
{
    private readonly ConcurrentQueue<string> _replies;
    private readonly string _fallbackReply;
    private int _invocationCount;
    private string? _failWithMessage;

    public ScriptedChatClient(params string[] replies)
    {
        _replies = new ConcurrentQueue<string>(replies);
        _fallbackReply = replies.Length > 0 ? replies[^1] : "reply";
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public IReadOnlyList<string> SeenPromptFragments { get; } = new List<string>();

    public void FailNextWith(string message) => _failWithMessage = message;

    public void Reset()
    {
        Volatile.Write(ref _invocationCount, 0);
        _failWithMessage = null;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invocationCount);
        if (_failWithMessage is not null)
        {
            var message = _failWithMessage;
            _failWithMessage = null;
            throw new InvalidOperationException(message);
        }

        if (!_replies.TryDequeue(out var reply))
            reply = _fallbackReply;

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate(message.Role, message.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
