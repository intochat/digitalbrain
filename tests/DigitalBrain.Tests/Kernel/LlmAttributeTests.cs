using System.Reflection;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class LlmAttributeTests
{
    [Fact]
    public async Task MapperResolvesTheKeyedChatClientForTheAttributesModelType()
    {
        var services = new ServiceCollection();
        var expectedClient = new FakeChatClient();
        services.AddKeyedSingleton<IChatClient>(TestModel.ServiceKey, expectedClient);
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

    private sealed class TestModel
    {
        public const string ServiceKey = "test-provider-test-model";
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

    private sealed class FakeGrain(IChatClient chatClient) { }
    private sealed class FakeGrainWithWrongParameterType(string notAChatClient) { }

    // Minimal IGrainContext fake — Orleans.TestingHost (10.2.0) has no ready-made fake grain context to
    // reuse, so this hand-rolls the full member list of the installed Orleans.Runtime.IGrainContext
    // (verified via reflection against the restored 10.2.1-preview.1 assembly, not guessed), throwing
    // NotSupportedException for everything this test doesn't exercise beyond ActivationServices.
    private sealed class FakeGrainContext(IServiceProvider services) : IGrainContext
    {
        public IServiceProvider ActivationServices => services;
        public GrainReference GrainReference => throw new NotSupportedException();
        public GrainId GrainId => default;
        public object? GrainInstance => null;
        public ActivationId ActivationId => default;
        public GrainAddress Address => throw new NotSupportedException();
        public IGrainLifecycle ObservableLifecycle => throw new NotSupportedException();
        public IWorkItemScheduler Scheduler => throw new NotSupportedException();
        public Task Deactivated => throw new NotSupportedException();

        public void SetComponent<TComponent>(TComponent? value) where TComponent : class => throw new NotSupportedException();
        public void ReceiveMessage(object message) => throw new NotSupportedException();
        public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Rehydrate(IRehydrationContext context) => throw new NotSupportedException();
        public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        object? ITargetHolder.GetTarget() => throw new NotSupportedException();
        object? ITargetHolder.GetComponent(Type componentType) => throw new NotSupportedException();
        bool IEquatable<IGrainContext>.Equals(IGrainContext? other) => ReferenceEquals(this, other);
    }
}
