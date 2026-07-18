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
    private bool _hangUntilCancelled;
    private int _cancellationObservedCount;

    public ScriptedChatClient(params string[] replies)
    {
        _replies = new ConcurrentQueue<string>(replies);
        _fallbackReply = replies.Length > 0 ? replies[^1] : "reply";
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public int CancellationObservedCount => Volatile.Read(ref _cancellationObservedCount);

    public void FailNextWith(string message) => _failWithMessage = message;

    public void HangUntilCancelled() => _hangUntilCancelled = true;

    public void Reset()
    {
        Volatile.Write(ref _invocationCount, 0);
        Volatile.Write(ref _cancellationObservedCount, 0);
        _failWithMessage = null;
        _hangUntilCancelled = false;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invocationCount);
        if (_hangUntilCancelled)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancellationObservedCount);
                throw;
            }
        }

        if (_failWithMessage is not null)
        {
            var message = _failWithMessage;
            _failWithMessage = null;
            throw new InvalidOperationException(message);
        }

        if (!_replies.TryDequeue(out var reply))
            reply = _fallbackReply;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply));
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
