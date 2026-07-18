using Anthropic.Exceptions;
using DigitalBrain;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Runtime.CompilerServices;
using Xunit;

namespace DigitalBrain.Tests.AI;

public sealed class ConversationRoleInvokerTests
{
    [Theory]
    [InlineData(AmbiguousProviderFailure.AnthropicSse)]
    [InlineData(AmbiguousProviderFailure.AnthropicInvalidData)]
    [InlineData(AmbiguousProviderFailure.OpenAiWithoutResponse)]
    public async Task Post_dispatch_provider_failures_are_classified_as_unknown(
        AmbiguousProviderFailure failureKind)
    {
        var providerFailure = CreateAmbiguousFailure(failureKind);
        var invoker = CreateInvoker(providerFailure);

        var failure = await Assert.ThrowsAsync<ProviderInvocationException>(() =>
            invoker.CompleteAsync(
                ConversationRole.Fast,
                "hello",
                CancellationToken.None));

        Assert.True(failure.OutcomeUnknown);
        Assert.Same(providerFailure, failure.InnerException);
    }

    [Theory]
    [InlineData(ConfirmedProviderFailure.AnthropicBadRequest)]
    [InlineData(ConfirmedProviderFailure.OpenAiRateLimit)]
    public async Task Confirmed_http_rejections_are_classified_as_failed(
        ConfirmedProviderFailure failureKind)
    {
        var providerFailure = CreateConfirmedFailure(failureKind);
        var invoker = CreateInvoker(providerFailure);

        var failure = await Assert.ThrowsAsync<ProviderInvocationException>(() =>
            invoker.CompleteAsync(
                ConversationRole.Fast,
                "hello",
                CancellationToken.None));

        Assert.False(failure.OutcomeUnknown);
        Assert.Same(providerFailure, failure.InnerException);
    }

    private static Exception CreateAmbiguousFailure(AmbiguousProviderFailure failureKind) =>
        failureKind switch
        {
            AmbiguousProviderFailure.AnthropicSse =>
                new AnthropicSseException("partial stream"),
            AmbiguousProviderFailure.AnthropicInvalidData =>
                new AnthropicInvalidDataException("malformed payload"),
            AmbiguousProviderFailure.OpenAiWithoutResponse =>
                new ClientResultException("response unavailable"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
        };

    private static Exception CreateConfirmedFailure(ConfirmedProviderFailure failureKind) =>
        failureKind switch
        {
            ConfirmedProviderFailure.AnthropicBadRequest =>
                new AnthropicApiException("bad request")
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ResponseBody = "{}"
                },
            ConfirmedProviderFailure.OpenAiRateLimit =>
                new ClientResultException(new TestPipelineResponse(429)),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
        };

    public enum AmbiguousProviderFailure
    {
        AnthropicSse,
        AnthropicInvalidData,
        OpenAiWithoutResponse
    }

    public enum ConfirmedProviderFailure
    {
        AnthropicBadRequest,
        OpenAiRateLimit
    }

    private static ConversationRoleInvoker CreateInvoker(Exception failure)
    {
        var client = new ThrowingChatClient(failure);
        return new ConversationRoleInvoker(
            new FastModelClient(client),
            new BalancedModelClient(client),
            new ReasoningModelClient(client));
    }

    private sealed class ThrowingChatClient(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(failure);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class TestPipelineResponse(int status) : PipelineResponse
    {
        private BinaryData _content = BinaryData.FromString("{}");

        public override BinaryData Content => _content;

        public override Stream? ContentStream { get; set; }

        protected override PipelineResponseHeaders HeadersCore { get; } =
            new EmptyPipelineResponseHeaders();

        public override string ReasonPhrase => "Test response";

        public override int Status => status;

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) =>
            _content;

        public override ValueTask<BinaryData> BufferContentAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_content);

        public override void Dispose()
        {
            ContentStream?.Dispose();
        }
    }

    private sealed class EmptyPipelineResponseHeaders : PipelineResponseHeaders
    {
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();

        public override bool TryGetValue(string name, out string? value)
        {
            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }
}
