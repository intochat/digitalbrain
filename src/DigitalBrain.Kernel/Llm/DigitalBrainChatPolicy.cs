using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Llm;

public sealed record DigitalBrainChatPolicyOptions(int MaximumConcurrency, TimeSpan RequestTimeout)
{
    public static DigitalBrainChatPolicyOptions Default { get; } = new(4, TimeSpan.FromSeconds(90));

    internal void Validate()
    {
        if (MaximumConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumConcurrency));
        if (RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
    }
}

internal sealed class BoundedNoRetryChatClient : DelegatingChatClient
{
    private readonly SemaphoreSlim _concurrency;
    private readonly TimeSpan _requestTimeout;

    public BoundedNoRetryChatClient(IChatClient innerClient, DigitalBrainChatPolicyOptions options)
        : base(innerClient)
    {
        options.Validate();
        _concurrency = new SemaphoreSlim(options.MaximumConcurrency, options.MaximumConcurrency);
        _requestTimeout = options.RequestTimeout;
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(token => base.GetResponseAsync(messages, options, token), cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        await _concurrency.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, deadline.Token)
                .WithCancellation(deadline.Token)
                .ConfigureAwait(false))
            {
                yield return update;
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        await _concurrency.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            return await action(deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            _concurrency.Release();
        }
    }
}
