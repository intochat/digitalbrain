using System.Threading;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests.Llm;

public sealed class DigitalBrainChatPolicyTests
{
    [Fact]
    public async Task Wrap_limits_concurrent_responses()
    {
        var inner = new BlockingChatClient();
        using var client = DigitalBrainChatTelemetry.Wrap(
            inner,
            new DigitalBrainChatPolicyOptions(1, TimeSpan.FromSeconds(5)));

        var first = client.GetResponseAsync(Messages("first"));
        await inner.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(2));
        var second = client.GetResponseAsync(Messages("second"));

        try
        {
            Assert.Equal(1, inner.CallCount);
        }
        finally
        {
            inner.ReleaseFirstCall();
        }

        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Wrap_does_not_retry_a_failed_response()
    {
        var inner = new FailingChatClient();
        using var client = DigitalBrainChatTelemetry.Wrap(
            inner,
            new DigitalBrainChatPolicyOptions(1, TimeSpan.FromSeconds(5)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(Messages("fail")));

        Assert.Equal(1, inner.CallCount);
    }

    private static ChatMessage[] Messages(string text) => [new(ChatRole.User, text)];

    private sealed class BlockingChatClient : IChatClient
    {
        private readonly TaskCompletionSource _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public Task FirstCallStarted => _firstCallStarted.Task;
        public int CallCount => Volatile.Read(ref _callCount);

        public void ReleaseFirstCall() => _releaseFirstCall.TrySetResult();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                _firstCallStarted.TrySetResult();
                await _releaseFirstCall.Task.WaitAsync(cancellationToken);
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "response"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FailingChatClient : IChatClient
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromException<ChatResponse>(new InvalidOperationException("provider failure"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
