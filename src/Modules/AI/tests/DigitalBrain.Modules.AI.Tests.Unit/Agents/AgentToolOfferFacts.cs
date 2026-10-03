using System.Runtime.CompilerServices;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class AgentToolOfferFacts
{
    [Fact]
    public async Task AnOfferedToolJoinsTheRestOfTheTurn()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = new ServiceCollection().AddSingleton<IChatClient>(FindThenCallClient())
            .AddSingleton<IAgentToolFactory>(new OfferingTools()).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();

        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["find"]), ct))
        { events.Add(item); }

        Assert.Equal(["find", "extra"], events.OfType<AgentTurnEvent.ToolCompleted>().Select(completed => completed.Name));
        Assert.Contains("extra ran", events.OfType<AgentTurnEvent.Text>().Last().Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoveryResolvesSourceToolsWithLiveContextAndDisposesSessions()
    {
        var source = new DynamicSource();
        using var services = new ServiceCollection().AddSingleton<IChatClient>(FindThenCallClient())
            .AddSingleton<IAgentToolFactory>(new DiscoveryTools("extra"))
            .AddSingleton<IAgentToolSource>(source).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "postgres", null, ToolNames: ["find"]), TestContext.Current.CancellationToken))
        { events.Add(item); }
        Assert.Empty(events.OfType<AgentTurnEvent.Failed>());
        Assert.Equal("call-2", source.InvokedContext?.CallId);
        Assert.Null(source.InvokedContext?.DatabaseSource);
        Assert.Equal(source.Opened, source.Disposed);
        Assert.Equal(["find", "extra"], events.OfType<AgentTurnEvent.ToolCompleted>().Select(item => item.Name));
    }

    [Fact]
    public async Task UnknownOffersFailClearly()
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(FindThenCallClient())
            .AddSingleton<IAgentToolFactory>(new DiscoveryTools("missing")).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["find"]), TestContext.Current.CancellationToken))
        { events.Add(item); }
        Assert.Contains("missing", Assert.Single(events.OfType<AgentTurnEvent.Failed>()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableCallsReturnAnErrorWithoutInvokingUnselectedTools()
    {
        var client = new StubChatClient((messages, _, _) => Task.FromResult(new ChatResponse(
            messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Any()
                ? new ChatMessage(ChatRole.Assistant, "recovered")
                : new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("missing-call", "extra", new Dictionary<string, object?>())]))));
        using var services = new ServiceCollection().AddSingleton<IChatClient>(client)
            .AddSingleton<IAgentToolFactory>(new OfferingTools()).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["find"]), TestContext.Current.CancellationToken))
        { events.Add(item); }
        Assert.Empty(events.OfType<AgentTurnEvent.Failed>());
        Assert.Single(events.OfType<AgentTurnEvent.ToolStarted>());
        Assert.Contains(events.OfType<AgentTurnEvent.Text>(), item => item.Content == "recovered");
        var completed = Assert.Single(events.OfType<AgentTurnEvent.Completed>());
        Assert.Contains("tool_unavailable", System.Text.Json.JsonSerializer.Serialize(completed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserWordsDoNotFilterExplicitlySelectedTools()
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new StubChatClient((_, options, _) =>
        {
            Assert.Contains(options!.Tools!, tool => tool.Name == "supabase_schema");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        })).AddSingleton<IAgentToolFactory>(new NamedTools()).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "postgres", null, ToolNames: ["supabase_schema"]), TestContext.Current.CancellationToken))
        { events.Add(item); }
        Assert.Empty(events.OfType<AgentTurnEvent.Failed>());
        Assert.Single(events.OfType<AgentTurnEvent.Finished>());
    }

    private sealed class NamedTools : IAgentToolFactory
    {
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context) => [AIFunctionFactory.Create(() => "schema", "supabase_schema")];
    }

    private sealed class DiscoveryTools(string offered) : IAgentToolFactory
    {
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context) =>
        [AIFunctionFactory.Create(() => new AgentToolOffer([offered], "found"), new AIFunctionFactoryOptions
        {
            Name = "find", MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
        })];
    }

    private sealed class DynamicSource : IAgentToolSource
    {
        public int Opened;
        public int Disposed;
        public AgentToolContext? InvokedContext;
        public Task<IAgentToolSession> OpenAsync(IReadOnlyList<string> selectedToolNames, Func<AgentToolContext> context, CancellationToken ct)
        {
            Opened++;
            IReadOnlyList<AIFunction> tools = selectedToolNames.Contains("extra")
                ? [AIFunctionFactory.Create(() => { InvokedContext = context(); return "extra ran"; }, "extra")] : [];
            return Task.FromResult<IAgentToolSession>(new Session(tools, () => Disposed++));
        }
        private sealed class Session(IReadOnlyList<AIFunction> tools, Action dispose) : IAgentToolSession
        {
            public IReadOnlyList<AIFunction> Tools => tools;
            public ValueTask DisposeAsync() { dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class OfferingTools : IAgentToolFactory
    {
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context) =>
        [
            AIFunctionFactory.Create(() => new AgentToolOffer(["extra"], "found extra"), new AIFunctionFactoryOptions
            {
                Name = "find",
                MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
            }),
            AIFunctionFactory.Create(() => "extra ran", "extra"),
        ];
    }

    private static StubChatClient FindThenCallClient() => new((messages, _, _) =>
    {
        var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
        var reply = results.Length switch
        {
            0 => new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "find", new Dictionary<string, object?>())]),
            1 => new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-2", "extra", new Dictionary<string, object?>())]),
            _ => new ChatMessage(ChatRole.Assistant, results[^1].Result!.ToString()),
        };
        return Task.FromResult(new ChatResponse(reply));
    });
}
