using System.Runtime.CompilerServices;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;

namespace DigitalBrain.ModuleTests;

internal static class ChatEdgeExtensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The scripted client has empty disposal and outlives the builder configuration.")]
    internal static void ConfigureChatEdge(this DigitalBrainTestBuilder builder)
    {
        var chat = new ChatEdgeScript();
        builder.ConfigureChatClient<IChatClient, ChatEdgeScript>(
            [typeof(Llama32)],
            new ScriptedChatClient(chat),
            chat,
            static script => script.Reset());
    }

    internal static ChatEdgeScript Chat(this TestBrain brain)
        => brain.ChatClientScript<ChatEdgeScript>();
}

internal sealed record ChatCall(
    IReadOnlyList<ChatMessage> Messages,
    ChatOptions? Options);

internal sealed class ChatEdgeScript
{
    private readonly Lock _gate = new();
    private readonly List<ChatCall> _calls = [];
    private readonly Queue<string> _replies = [];

    internal IReadOnlyList<ChatCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    internal void Reply(string text)
    {
        lock (_gate)
        {
            _replies.Enqueue(text);
        }
    }

    internal Task<ChatResponse> Respond(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string text;
        lock (_gate)
        {
            var snapshot = messages
                .Select(message => new ChatMessage(
                    message.Role,
                    message.Contents
                        .Select(content => content)
                        .ToArray()))
                .ToArray();
            _calls.Add(new(snapshot, options));
            text = _replies.Count > 0
                ? _replies.Dequeue()
                : $"reply-{_calls.Count}";
        }

        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, text)));
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            _replies.Clear();
        }
    }
}

internal sealed class ScriptedChatClient(ChatEdgeScript script) : IChatClient
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
        var response = await script.Respond(
            messages,
            options,
            cancellationToken);

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
