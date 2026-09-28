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
    public void DefaultAllowlistIsAtMostEightToolsAndExcludesCSharpTools()
    {
        var selected = AgentToolPolicy.SelectTools(developerMode: false,
            ["csharp_contracts", "csharp_write", "csharp_run"]);
        Assert.True(selected.Count <= AgentToolPolicy.MaxDefaultTools);
        Assert.DoesNotContain(selected, AgentToolPolicy.IsCSharpTool);
        Assert.Equal(AgentToolPolicy.ProductTools, selected);
    }

    [Fact]
    public void RelevantAppToolsJoinTheCoreAndNoneOfThemExceedTheCap()
    {
        var selected = AgentToolPolicy.SelectTools(developerMode: false, [],
            ["propose_app", "run_leadgenerator"]);
        Assert.True(selected.Count <= AgentToolPolicy.MaxDefaultTools);
        Assert.Contains("find_capability", selected);
        Assert.Contains("propose_app", selected);
        Assert.Contains("run_leadgenerator", selected);
        Assert.DoesNotContain("supabase_schema", selected);
    }

    [Fact]
    public void ATableIntentKeepsTheGenericTableToolsWhenAnAppIsRelevant()
    {
        var selected = AgentToolPolicy.SelectTools(developerMode: false, [],
            ["propose_app"], tableIntent: true);
        Assert.Contains("supabase_schema", selected);
        Assert.Contains("show_supabase_query_table", selected);
    }

    [Fact]
    public void ManyRelevantAppToolsStillFitTheCap()
    {
        var selected = AgentToolPolicy.SelectTools(developerMode: false, [],
            ["propose_app", "run_leadgenerator", "plan_background_removal", "run_background_removal", "find_capability"]);
        Assert.Equal(AgentToolPolicy.MaxDefaultTools, selected.Count);
        Assert.Equal(selected.Count, selected.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ExplicitDeveloperModeIncludesCSharpTools()
    {
        string[] developerTools = ["csharp_contracts", "csharp_write", "csharp_run"];
        var selected = AgentToolPolicy.SelectTools(developerMode: true, developerTools);
        Assert.Equal(AgentToolPolicy.ProductTools.Concat(developerTools), selected);
        Assert.Contains("csharp_run", selected);
    }

    [Fact]
    public void DeveloperModeOffRefusesCSharpAuthoringWithOneLinePhase1Fallback()
    {
        var fallback = AgentToolPolicy.UnsupportedCSharpAuthoring(developerMode: false, "write C# code that saves invoices");
        Assert.NotNull(fallback);
        Assert.Equal(AgentToolPolicy.CSharpAuthoringFallback, fallback);
        Assert.Contains("Phase 1", fallback!);
        Assert.DoesNotContain('\n', fallback!);
    }

    [Fact]
    public void DeveloperModeOffStillAnswersOrdinaryTableRequests()
    {
        Assert.Null(AgentToolPolicy.UnsupportedCSharpAuthoring(developerMode: false, "show me all customers"));
        Assert.Equal(AgentToolPolicy.ProductTools, AgentToolPolicy.SelectTools(developerMode: false, ["csharp_run"]));
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
    [Fact]
    public async Task SelectedContextProvidersReachTheModelBeforeTheMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new RecordingClient();
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

    private sealed class FixedContext(string name, string context) : IAgentContextProvider
    {
        public string Name => name;
        public Task<string?> Provide(AgentContextRequest request, CancellationToken ct) => Task.FromResult<string?>(context);
    }

    private sealed class FailingContext : IAgentContextProvider
    {
        public string Name => "failing";
        public Task<string?> Provide(AgentContextRequest request, CancellationToken ct) => throw new InvalidOperationException("down");
    }

    private sealed class RecordingClient : IChatClient
    {
        public List<ChatMessage[]> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

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