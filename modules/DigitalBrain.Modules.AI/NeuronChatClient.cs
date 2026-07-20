using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class NeuronChatClient(ILLM model, TaskScheduler? turnScheduler = null) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var request = Request(messages, options);

        return turnScheduler is null
            ? model.RespondAsync(request)
            : Task.Factory.StartNew(
                () => model.RespondAsync(request),
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
