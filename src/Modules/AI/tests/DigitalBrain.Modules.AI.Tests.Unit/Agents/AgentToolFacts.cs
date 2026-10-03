using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class AgentToolFacts
{
    [Fact]
    public async Task ToolDoesNotExecuteUntilCallerHasRecordedItsStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var tool = new ObservedStartTools();
        using var services = new ServiceCollection().AddSingleton<IChatClient>(ScriptedClient())
            .AddSingleton<IAgentToolFactory>(tool).BuildServiceProvider();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["lookup"]), ct))
        {
            if (item is AgentTurnEvent.ToolStarted)
            {
                Assert.False(tool.Executed);
                tool.Recorded = true;
            }
        }
        Assert.True(tool.Executed);
    }

    private sealed class ObservedStartTools : IAgentToolFactory
    {
        public bool Recorded { get; set; }
        public bool Executed { get; private set; }
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context) =>
            [AIFunctionFactory.Create(() => { Executed = true; return Recorded ? "recorded" : "unrecorded"; }, "lookup")];
    }

    [Fact]
    public async Task ToolsUseActualCallIdentityAndNeverShareRunContext()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = new ServiceCollection().AddSingleton<IChatClient>(ScriptedClient())
            .AddSingleton<IAgentToolFactory, Tools>().BuildServiceProvider();
        var runner = new AgentTurnRunner(services);
        async Task<string[]> Run(string scope)
        {
            var events = new List<AgentTurnEvent>();
            await foreach (var item in runner.RunAsync(new("agent", "run-" + scope, scope, [], "ask", null, ToolNames: ["lookup"]), ct)) { events.Add(item); }
            Assert.Single(events.OfType<AgentTurnEvent.ToolCompleted>());
            return events.OfType<AgentTurnEvent.Text>().Select(e => e.Content).ToArray();
        }
        var results = await Task.WhenAll(Run("alpha"), Run("beta"));
        Assert.Contains("alpha/run-alpha/actual-call", Assert.Single(results[0]));
        Assert.Contains("beta/run-beta/actual-call", Assert.Single(results[1]));
    }

    [Fact]
    public async Task MissingSelectedToolFailsExplicitly()
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(ScriptedClient()).BuildServiceProvider();
        var runner = new AgentTurnRunner(services);
        var events = new List<AgentTurnEvent>();
        await foreach (var item in runner.RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["missing"]), TestContext.Current.CancellationToken)) { events.Add(item); }
        Assert.Single(events.OfType<AgentTurnEvent.Failed>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
    }

    [Fact]
    public async Task DelayedToolFailureNeverFinishesTheRun()
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(ScriptedClient())
            .AddSingleton<IAgentToolFactory>(new Tools(fail: true)).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["lookup"]), TestContext.Current.CancellationToken)) { events.Add(item); }
        Assert.Single(events.OfType<AgentTurnEvent.Failed>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
        Assert.Empty(events.OfType<AgentTurnEvent.ToolCompleted>());
    }
    private sealed class Tools(bool fail = false) : IAgentToolFactory
    {
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context) =>
            [AIFunctionFactory.Create(async () => { await Task.Yield(); if (fail) { throw new IOException("Tool failed after starting"); } var value = context(); return value.ScopeId + "/" + value.RunId + "/" + value.CallId; }, "lookup")];
    }
    [Fact]
    public async Task RepeatedUnavailableCallsStopAtTheModelCallBudget()
    {
        var calls = 0;
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new StubChatClient((_, _, _) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("unknown-" + ++calls, "unknown", new Dictionary<string, object?>())])))))
            .AddSingleton<IAgentToolFactory, Tools>().BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["lookup"], MaxModelCalls: 2), TestContext.Current.CancellationToken)) { events.Add(item); }
        Assert.Contains("budget", Assert.Single(events.OfType<AgentTurnEvent.Failed>()).Message, StringComparison.Ordinal);
        Assert.Equal(2, calls);
        Assert.Equal(2, events.OfType<AgentTurnEvent.ToolFailed>().Count());
        Assert.Empty(events.OfType<AgentTurnEvent.ToolCompleted>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
    }

    [Fact]
    public async Task SelectedContextProvidersReachTheModelBeforeTheMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = StubChatClient.Replying("done");
        using var services = new ServiceCollection().AddSingleton<IChatClient>(client)
            .AddSingleton<IAgentContextProvider>(new FixedContext("capabilities", "Available: invoices"))
            .AddSingleton<IAgentContextProvider>(new FixedContext("unselected", "must not appear"))
            .AddSingleton<IAgentContextProvider>(new FailingContext())
            .BuildServiceProvider();

        await foreach (var _ in new AgentTurnRunner(services).RunAsync(
            new("agent", "run", "scope", [], "ask", null, ContextProviders: ["capabilities", "failing"]), ct)) { }

        var messages = Assert.Single(client.Requests);
        Assert.Equal([ChatRole.System, ChatRole.User], messages.Select(message => message.Role));
        Assert.Equal("Available: invoices", messages[0].Text);
    }

    [Fact]
    public async Task ContextToolsJoinTheTurnOnlyWhenTheHostRegistersThem()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = new ServiceCollection().AddSingleton<IChatClient>(ScriptedClient())
            .AddSingleton<IAgentToolFactory, Tools>()
            .AddSingleton<IAgentContextProvider>(new FixedContext("capabilities", "Available: lookup", ["lookup", "not_registered"]))
            .BuildServiceProvider();
        var events = new List<AgentTurnEvent>();

        await foreach (var item in new AgentTurnRunner(services).RunAsync(
            new("agent", "run", "scope", [], "ask", null, ContextProviders: ["capabilities"]), ct)) { events.Add(item); }

        Assert.Equal("lookup", Assert.Single(events.OfType<AgentTurnEvent.ToolCompleted>()).Name);
    }

    private sealed class FixedContext(string name, string text, IReadOnlyList<string>? tools = null) : IAgentContextProvider
    {
        public string Name => name;
        public Task<AgentContext> Provide(AgentContextRequest request, CancellationToken ct) => Task.FromResult(new AgentContext(text, tools ?? []));
    }

    private sealed class FailingContext : IAgentContextProvider
    {
        public string Name => "failing";
        public Task<AgentContext> Provide(AgentContextRequest request, CancellationToken ct) => throw new InvalidOperationException("down");
    }

    private static StubChatClient ScriptedClient(string toolName = "lookup") => new((messages, _, _) =>
    {
        var result = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
        return Task.FromResult(new ChatResponse(result is null
            ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("actual-call", toolName, new Dictionary<string, object?>())])
            : new ChatMessage(ChatRole.Assistant, result.Result!.ToString())));
    });
}
