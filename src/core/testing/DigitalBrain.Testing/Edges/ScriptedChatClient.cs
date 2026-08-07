using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Testing;

public sealed class ScriptedChatClient : IChatClient
{
    private readonly Lock _gate = new();
    private readonly Queue<ChatMessage> _replies = [];
    private int _callCount;
    private ChatMessage[] _lastMessages = [];
    private string[] _lastTools = [];

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _callCount;
            }
        }
    }

    public IReadOnlyList<ChatMessage> LastMessages
    {
        get
        {
            lock (_gate)
            {
                return [.. _lastMessages];
            }
        }
    }

    public IReadOnlyList<string> LastTools
    {
        get
        {
            lock (_gate)
            {
                return [.. _lastTools];
            }
        }
    }

    public void Reply(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Enqueue(new ChatMessage(ChatRole.Assistant, text));
    }

    public void ReplyWithCapabilityCall(string tool, IDictionary<string, object?> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentNullException.ThrowIfNull(arguments);

        Enqueue(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent(Guid.NewGuid().ToString("N"), tool, arguments)]));
    }

    public void Reset()
    {
        lock (_gate)
        {
            _callCount = 0;
            _lastMessages = [];
            _lastTools = [];
            _replies.Clear();
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(NextResponse(messages, options, cancellationToken));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private void Enqueue(ChatMessage reply)
    {
        lock (_gate)
        {
            _replies.Enqueue(reply);
        }
    }

    private ChatResponse NextResponse(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        ChatMessage reply;
        var request = messages.ToArray();
        var offered = options?.Tools is { } tools ? tools.Select(tool => tool.Name).ToArray() : [];
        lock (_gate)
        {
            _callCount++;
            _lastMessages = request;
            _lastTools = offered;
            reply = _replies.Count > 0
                ? _replies.Dequeue()
                : throw new InvalidOperationException(
                    "The scripted chat client ran out of replies. Script one reply per model call, including the call that follows a capability invocation.");
        }

        return new ChatResponse(reply);
    }
}
