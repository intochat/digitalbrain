using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class NeuronChatClient(INeuron participant, TaskScheduler turnScheduler) : IChatClient
{
    private readonly Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>> _invoke = InvocationFor(participant);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var request = Request(messages, options);
        var response = Task.Factory.StartNew(
            () => _invoke(request),
            cancellationToken,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap();

        ObserveFault(response);
        return response.WaitAsync(cancellationToken);
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

    private static void ObserveFault(Task response)
        => _ = response.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private static Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>> InvocationFor(INeuron participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return participant switch
        {
            ILLM model => model.Respond,
            IAgent agent => agent.Respond,
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
