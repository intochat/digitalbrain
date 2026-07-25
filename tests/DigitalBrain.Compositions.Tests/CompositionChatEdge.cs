using System.Runtime.CompilerServices;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Compositions.Tests;

internal static class CompositionChatEdge
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The scripted client has empty disposal and outlives the builder configuration.")]
    internal static void Configure(DigitalBrainTestBuilder builder)
    {
        var chat = new CompositionChatScript();
        builder.ConfigureChatClient<IChatClient, CompositionChatScript>(
            [typeof(Llama32)],
            new ScriptedChatClient(chat),
            chat,
            static script => script.Reset());
    }

    internal static CompositionChatScript Chat(this TestBrain brain)
        => brain.ChatClientScript<CompositionChatScript>();
}

internal sealed class CompositionChatScript
{
    private readonly Lock _gate = new();
    private readonly Queue<string> _replies = [];
    private int _callCount;

    internal void Reply(string text)
    {
        lock (_gate)
        {
            _replies.Enqueue(text);
        }
    }

    internal Task<ChatResponse> Respond(
        IEnumerable<ChatMessage> messages,
        ChatOptions? _,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        string text;
        lock (_gate)
        {
            _callCount++;
            text = _replies.Count > 0
                ? _replies.Dequeue()
                : $"reply-{_callCount}";
        }

        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, text)));
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _callCount = 0;
            _replies.Clear();
        }
    }
}

file sealed class ScriptedChatClient(CompositionChatScript script) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => script.Respond(messages, options, cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await script.Respond(messages, options, cancellationToken);
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
}
