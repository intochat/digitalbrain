using DigitalBrain.Kernel.Contracts.Models;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using OllamaModels = DigitalBrain.Kernel.Contracts.Models.Ollama;

namespace DigitalBrain.Tests.Kernel;

public class LlmAttributeTests
{
    private const string TestModelServiceKey = "test-provider-test-model";

    [Fact]
    public async Task MapperResolvesTheKeyedChatClientForTheAttributesModelType()
    {
        var services = new ServiceCollection();
        var expectedClient = new FakeChatClient();
        services.AddKeyedSingleton<IChatClient>(TestModelServiceKey, expectedClient);
        var provider = services.BuildServiceProvider();

        var mapper = new LlmAttributeMapper<TestModel>();
        var parameter = typeof(FakeGrain).GetConstructors()[0].GetParameters()[0];
        var factory = mapper.GetFactory(parameter, new LlmAttribute<TestModel>());

        var context = new FakeGrainContext(provider);
        var resolved = factory(context);

        Assert.Same(expectedClient, resolved);
        await Task.CompletedTask;
    }

    [Fact]
    public void ThrowsAClearErrorWhenAppliedToTheWrongParameterType()
    {
        var mapper = new LlmAttributeMapper<TestModel>();
        var parameter = typeof(FakeGrainWithWrongParameterType).GetConstructors()[0].GetParameters()[0];

        var ex = Assert.Throws<ArgumentException>(() => mapper.GetFactory(parameter, new LlmAttribute<TestModel>()));

        Assert.Contains("IChatClient", ex.Message);
    }

    [Fact]
    public void ResolvesTheKeyedChatClientForARealProductionModelType()
    {
        var services = new ServiceCollection();
        var expectedClient = new FakeChatClient();
        var realModelServiceKey = new OllamaModels.Llama31_8B().Describe().ServiceKey;
        services.AddKeyedSingleton<IChatClient>(realModelServiceKey, expectedClient);
        var provider = services.BuildServiceProvider();

        Assert.Equal(realModelServiceKey, LlmServiceKeys.For(typeof(OllamaModels.Llama31_8B)));

        var mapper = new LlmAttributeMapper<OllamaModels.Llama31_8B>();
        var parameter = typeof(FakeGrain).GetConstructors()[0].GetParameters()[0];
        var factory = mapper.GetFactory(parameter, new LlmAttribute<OllamaModels.Llama31_8B>());

        var resolved = factory(new FakeGrainContext(provider));

        Assert.Same(expectedClient, resolved);
    }

    private sealed class TestModel : LlmModel
    {
        public override string Provider => "test-provider";
        public override string Id => "test-model";
    }

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeGrain
    {
        public FakeGrain(IChatClient chatClient) => _ = chatClient;
    }

    private sealed class FakeGrainWithWrongParameterType
    {
        public FakeGrainWithWrongParameterType(string notAChatClient) => _ = notAChatClient;
    }
}
