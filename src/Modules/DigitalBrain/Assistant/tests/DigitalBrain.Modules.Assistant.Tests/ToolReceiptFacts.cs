using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Assistant;
using DigitalBrain.Compute;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests;

public sealed class ToolReceiptFacts
{
    [Theory]
    [InlineData(false, "tool_unavailable")]
    [InlineData(true, "tool_failed")]
    public async Task RealRunnerFailuresReachReceiptsWithOneCallAndAReason(bool registered, string code)
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new CallingModel())
            .AddSingleton<IAgentToolFactory>(new FailingTool()).BuildServiceProvider();
        var activity = new IntentActivity();
        var emitted = new List<JsonElement>();
        var runner = new AgentTurnRunner(services);
        async Task Run() => await AssistantTurnExecution.RunModel("scope", "run", "hello",
            new() { Tools = registered ? ["broken"] : [] }, new(0, null, [], null), "reply",
            new(), runner.RunAsync, value => { emitted.Add(JsonSerializer.SerializeToElement(value)); return Task.CompletedTask; },
            new StringBuilder(), [], activity, TestContext.Current.CancellationToken);
        if (registered) { await Assert.ThrowsAsync<InvalidOperationException>(Run); }
        else { await Run(); }
        using var intent = IntentContext.Begin("receipt", "scope");
        var receipt = AgentReceipts.Create(new PriceBook(), intent, activity, registered ? AgentRunOutcome.Failed : AgentRunOutcome.Succeeded);
        var call = Assert.Single(receipt.Calls);
        Assert.False(call.Succeeded);
        Assert.Equal("attempt", call.CallId);
        Assert.Equal(code, call.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(call.ErrorMessage));
        Assert.Single(emitted, item => item.GetProperty("type").GetString() == "TOOL_CALL_START");
        Assert.Single(emitted, item => item.GetProperty("type").GetString() == "TOOL_CALL_RESULT");
        // This is the durable usage projection consumed by the Flutter details view.
        var stored = JsonSerializer.Serialize(AssistantUsage.Create("run", receipt, new PriceBook(), []));
        Assert.Contains(code, stored);
        Assert.Contains(call.ErrorMessage, stored);
    }

    private sealed class FailingTool : IAgentToolFactory
    {
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
        {
            static string Fail() => throw new InvalidOperationException("The selected resource changed.");
            return [AIFunctionFactory.Create(Fail, "broken")];
        }
    }
    private sealed class CallingModel : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any()
                ? new ChatMessage(ChatRole.Assistant, "Recovered")
                : new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("attempt", "broken", new Dictionary<string, object?>())])));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates()) { yield return update; }
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
