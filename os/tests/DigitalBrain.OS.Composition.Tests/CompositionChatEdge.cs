using System.Runtime.CompilerServices;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Compositions.Tests;

internal static class CompositionChatEdge
{
    internal static void Configure(DigitalBrainTestBuilder builder)
    {
#pragma warning disable CA2000 // Empty Dispose; edge outlives builder configuration
        var chat = new CompositionChatScript();
        builder.ConfigureChatClient<IChatClient, CompositionChatScript>(
            [typeof(Llama32)],
            chat,
            chat,
            static script => script.Reset());
#pragma warning restore CA2000
    }

    internal static CompositionChatScript Chat(this TestBrain brain)
        => brain.ChatClientScript<CompositionChatScript>();
}

internal sealed class CompositionChatScript : IChatClient
{
    private readonly Lock _gate = new();
    private readonly Queue<string> _replies = [];

    internal void Reply(string text)
    {
        lock (_gate)
        {
            _replies.Enqueue(text);
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(NextResponse(messages, cancellationToken));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _replies.Clear();
        }
    }

    private ChatResponse NextResponse(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        string text;
        lock (_gate)
        {
            text = _replies.Dequeue();
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }
}
