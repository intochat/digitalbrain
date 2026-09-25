using System.Runtime.CompilerServices;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentToolFacts
{
    [Fact]
    public async Task ToolDoesNotExecuteUntilCallerHasRecordedItsStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var tool = new ObservedStartTools();
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new ScriptedClient())
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
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new ScriptedClient())
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
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new ScriptedClient()).BuildServiceProvider();
        var runner = new AgentTurnRunner(services);
        var events = new List<AgentTurnEvent>();
        await foreach (var item in runner.RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["missing"]), TestContext.Current.CancellationToken)) { events.Add(item); }
        Assert.Single(events.OfType<AgentTurnEvent.Failed>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
    }

    [Fact]
    public async Task DelayedToolFailureNeverFinishesTheRun()
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new ScriptedClient())
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
    public async Task UnknownRequestedToolCannotBecomeSuccess()
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new ScriptedClient("unknown"))
            .AddSingleton<IAgentToolFactory, Tools>().BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["lookup"]), TestContext.Current.CancellationToken)) { events.Add(item); }
        Assert.Single(events.OfType<AgentTurnEvent.Failed>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
    }

    [Fact]
    public void DefaultAllowlistIsAtMostEightToolsAndExcludesBehaviorTools()
    {
        var selected = AgentToolPolicy.SelectTools(developerMode: false,
            ["code_contracts", "code_draft_save", "behavior_read", "behavior_deploy", "behavior_start"]);
        Assert.True(selected.Count <= AgentToolPolicy.MaxDefaultTools);
        Assert.DoesNotContain(selected, AgentToolPolicy.IsBehaviorTool);
        Assert.Contains("render_chart", selected);
        Assert.Equal(AgentToolPolicy.ProductTools, selected);
    }

    [Fact]
    public void ExplicitDeveloperModeIncludesBehaviorTools()
    {
        string[] developerTools = ["code_contracts", "behavior_read", "behavior_deploy"];
        var selected = AgentToolPolicy.SelectTools(developerMode: true, developerTools);
        Assert.Equal(AgentToolPolicy.ProductTools.Concat(developerTools), selected);
        Assert.Contains("behavior_read", selected);
    }

    [Fact]
    public void DeveloperModeOffRefusesBehaviorAuthoringWithOneLinePhase1Fallback()
    {
        var fallback = AgentToolPolicy.UnsupportedBehaviorAuthoring(developerMode: false, "write a C# behavior that saves invoices");
        Assert.NotNull(fallback);
        Assert.Equal(AgentToolPolicy.BehaviorAuthoringFallback, fallback);
        Assert.Contains("Phase 1", fallback!);
        Assert.DoesNotContain('\n', fallback!);
    }

    [Fact]
    public void DeveloperModeOffStillAnswersOrdinaryTableRequests()
    {
        Assert.Null(AgentToolPolicy.UnsupportedBehaviorAuthoring(developerMode: false, "show me all customers"));
        Assert.Equal(AgentToolPolicy.ProductTools, AgentToolPolicy.SelectTools(developerMode: false, ["behavior_read"]));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    public void DeveloperModeFailsClosedOnInvalidConfigurationButDefaultsOn(string? configured, bool expected)
        => Assert.Equal(expected, AgentToolPolicy.DeveloperModeEnabled(configured));
    private sealed class ScriptedClient(string toolName = "lookup") : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var result = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
            return Task.FromResult(new ChatResponse(result is null
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("actual-call", toolName, new Dictionary<string, object?>())])
                : new ChatMessage(ChatRole.Assistant, result.Result!.ToString())));
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.CompletedTask; yield break; }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
