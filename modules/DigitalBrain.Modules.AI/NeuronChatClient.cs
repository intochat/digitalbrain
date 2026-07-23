using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class NeuronChatClient(
    Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>> invoke,
    TaskScheduler? turnScheduler = null) : IChatClient
{
    internal NeuronChatClient(INeuron participant, TaskScheduler? turnScheduler = null)
        : this(InvocationFor(participant), turnScheduler)
    {
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var request = Request(messages, options);

        return turnScheduler is null
            ? invoke(request)
            : Task.Factory.StartNew(
                () => invoke(request),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                turnScheduler).Unwrap();
    }

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

    private static Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>> InvocationFor(
        INeuron participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return participant switch
        {
            ILLM model => model.RespondAsync,
            IAgent agent => agent.RespondAsync,
            _ => throw new ArgumentException(
                $"AI participant '{participant.GetType().FullName}' must implement {nameof(ILLM)} or {nameof(IAgent)}.",
                nameof(participant)),
        };
    }

    private static IReadOnlyList<ChatMessage> Request(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options)
    {
        var request = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();

        return string.IsNullOrWhiteSpace(options?.Instructions)
            ? request
            : [new ChatMessage(ChatRole.System, options.Instructions), .. request];
    }
}
