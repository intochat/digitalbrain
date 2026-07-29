using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class NeuronChatClient(
    INeuron participant,
    TaskScheduler turnScheduler,
    ParticipantInvocations invocations) : IChatClient
{
    private readonly Func<IReadOnlyList<ChatMessage>, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> _stream =
        StreamingInvocationFor(participant);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var updates = _stream(Request(messages, options), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            var carrying = await MoveNextOnTurnAsync(updates, cancellationToken);

            invocations.RecordInvocation();

            while (carrying)
            {
                yield return updates.Current;

                carrying = await MoveNextOnTurnAsync(updates, cancellationToken);
            }
        }
        finally
        {
            await DisposeOnTurnAsync(updates);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private Task<bool> MoveNextOnTurnAsync(
        IAsyncEnumerator<ChatResponseUpdate> updates,
        CancellationToken cancellationToken)
        => Task.Factory.StartNew(
            () => updates.MoveNextAsync().AsTask(),
            cancellationToken,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap();

    private Task DisposeOnTurnAsync(IAsyncEnumerator<ChatResponseUpdate> updates)
        => Task.Factory.StartNew(
            () => updates.DisposeAsync().AsTask(),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap();

    private static Func<IReadOnlyList<ChatMessage>, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>
        StreamingInvocationFor(INeuron participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return participant switch
        {
            ILLM model => model.RespondStreaming,
            IAgent agent => agent.RespondStreaming,
            _ => throw new ArgumentException(
                $"AI participant '{participant.GetType().FullName}' must implement {nameof(ILLM)} or {nameof(IAgent)}.",
                nameof(participant)),
        };
    }

    private static IReadOnlyList<ChatMessage> Request(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var request = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();

        return string.IsNullOrWhiteSpace(options?.Instructions)
            ? request
            : [new ChatMessage(ChatRole.System, options.Instructions), .. request];
    }
}
