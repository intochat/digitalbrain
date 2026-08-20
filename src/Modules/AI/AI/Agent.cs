using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public abstract class Agent : Neuron, IAgent
{
    private readonly IChatClient _chatClient;

    protected Agent(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        _chatClient = chatClient;
    }

    protected virtual string? Instructions => null;

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var options = new ChatOptions
        {
            MaxOutputTokens = 4096,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["num_ctx"] = 16384,
                ["think"] = false,
            },
        };
        var instructions = Instructions;
        IReadOnlyList<ChatMessage> request = string.IsNullOrWhiteSpace(instructions)
            ? messages
            : [new ChatMessage(ChatRole.System, instructions), .. messages];

        await foreach (var update in _chatClient
            .GetStreamingResponseAsync(request, options, cancellationToken).ConfigureAwait(true))
        {
            yield return update;
        }
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages, cancellationToken).ToChatResponseAsync(cancellationToken);
    }
}
