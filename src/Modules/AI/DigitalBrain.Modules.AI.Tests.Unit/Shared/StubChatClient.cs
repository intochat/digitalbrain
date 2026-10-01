using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Modules.AI.Tests.Unit;

internal delegate Task<ChatResponse> ChatResponder(IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken);

internal delegate IAsyncEnumerable<ChatResponseUpdate> ChatStreamer(IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken);

internal sealed class StubChatClient(ChatResponder? respond = null, ChatStreamer? stream = null) : IChatClient
{
    private readonly ConcurrentQueue<ChatMessage[]> requests = new();

    public IReadOnlyList<ChatMessage[]> Requests => [.. requests];

    public static StubChatClient Replying(string answer) =>
        new((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))));

    public static StubChatClient StreamingOnly(ChatStreamer stream) => new(stream: stream);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var recorded = Record(messages);
        return respond is null ? throw new NotSupportedException() : respond(recorded, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var recorded = Record(messages);
        return stream is null ? NoUpdates() : stream(recorded, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private ChatMessage[] Record(IEnumerable<ChatMessage> messages)
    {
        var recorded = messages.ToArray();
        requests.Enqueue(recorded);
        return recorded;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> NoUpdates()
    {
        await Task.CompletedTask;
        yield break;
    }
}
