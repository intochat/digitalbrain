using DigitalBrain.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorDecisionFacts
{
    private const string Schema = """{"type":"object","properties":{"route":{"type":"string","enum":["keep","drop"]}},"required":["route"],"additionalProperties":false}""";

    [Fact]
    public async Task Valid_decision_uses_structured_output_without_granting_tools()
    {
        using var model = new ScriptedChatClient();
        model.Say("""{"route":"keep"}""");
        using var services = new ServiceCollection().AddSingleton<IChatClient>(model).BuildServiceProvider();
        var result = await new BehaviorDecision(services).DecideAsync("Choose a route.", "input", Schema, null, null, TestContext.Current.CancellationToken);
        Assert.Equal("""{"route":"keep"}""", result);
        var options = Assert.Single(model.Options)!;
        Assert.Empty(options.Tools!);
        Assert.Equal(ChatToolMode.None, options.ToolMode);
        var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.Equal(Schema, format.Schema!.Value.GetRawText());
        var messages = Assert.Single(model.Calls);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal("input", messages[1].Text);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"route\":\"unexpected\"}")]
    [InlineData("{\"route\":\"keep\",\"extra\":true}")]
    public async Task Invalid_output_is_rejected_without_an_agent_retry(string output)
    {
        using var model = new ScriptedChatClient();
        model.Say(output);
        using var services = new ServiceCollection().AddSingleton<IChatClient>(model).BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BehaviorDecision(services)
            .DecideAsync("Choose.", "input", Schema, null, null, TestContext.Current.CancellationToken));
        Assert.Single(model.Calls);
    }

    [Fact]
    public async Task Function_call_response_is_rejected_even_with_valid_json_text()
    {
        using var model = new MixedResponseClient();
        using var services = new ServiceCollection().AddSingleton<IChatClient>(model).BuildServiceProvider();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new BehaviorDecision(services)
            .DecideAsync("Choose.", "input", Schema, null, null, TestContext.Current.CancellationToken));
        Assert.Contains("function call", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_with_function_invocation_middleware_is_rejected_before_any_model_request(bool nested)
    {
        using var model = new ScriptedChatClient();
        model.CallTool("mutate", "{}");
        using var wrapped = new FunctionInvokingChatClient(model);
        using var outer = new PassthroughChatClient(wrapped);
        using var services = new ServiceCollection().AddKeyedSingleton<IChatClient>("fixture", nested ? outer : wrapped).BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new BehaviorDecision(services)
            .DecideAsync("Choose.", "input", Schema, "fixture", "custom-model", TestContext.Current.CancellationToken));
        Assert.Contains("without function-invocation middleware", error.Message, StringComparison.Ordinal);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task Invalid_schema_is_rejected_before_requesting_the_model()
    {
        using var model = new ScriptedChatClient();
        using var services = new ServiceCollection().AddSingleton<IChatClient>(model).BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BehaviorDecision(services)
            .DecideAsync("Choose.", "input", "invalid", null, null, TestContext.Current.CancellationToken));
        Assert.Empty(model.Calls);
    }

    private sealed class PassthroughChatClient(IChatClient inner) : DelegatingChatClient(inner);

    private sealed class MixedResponseClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant,
                [new TextContent("""{"route":"keep"}"""), new FunctionCallContent("call", "mutate", new Dictionary<string, object?>())])]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
