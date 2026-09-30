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
    public async Task UnknownRequestedToolCannotBecomeSuccess()
    {
        using var services = new ServiceCollection().AddSingleton<IChatClient>(ScriptedClient("unknown"))
            .AddSingleton<IAgentToolFactory, Tools>().BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["lookup"]), TestContext.Current.CancellationToken)) { events.Add(item); }
        Assert.Single(events.OfType<AgentTurnEvent.Failed>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
    }

    [Fact]
    public void ExplicitComparisonKeepsBothSourcesWithInstalledApps()
    {
        var tools = AgentToolPolicy.SelectTools(false, [], ["run_customer_researcher"], message: "Compare Postgres and Supabase");
        Assert.Contains("postgres_schema", tools);
        Assert.Contains("show_postgres_query_table", tools);
        Assert.Contains("supabase_schema", tools);
        Assert.Contains("show_supabase_query_table", tools);
        Assert.True(tools.Count <= AgentToolPolicy.MaxDefaultTools);
    }

    [Theory]
    [InlineData("Show data from postgres", "postgres_schema", "supabase_schema")]
    [InlineData("Show data from PostgreSQL", "postgres_schema", "supabase_schema")]
    [InlineData("Show data from Supabase", "supabase_schema", "postgres_schema")]
    public void ExplicitDatabaseExcludesTheOtherDatabaseTools(string message, string expected, string forbidden)
    {
        var tools = AgentToolPolicy.SelectTools(developerMode: false, [], message: message);
        Assert.Contains(expected, tools);
        Assert.DoesNotContain(forbidden, tools);
    }

    [Fact]
    public void UnavailablePostgresDoesNotSubstituteSupabase()
    {
        var tools = AgentToolPolicy.ForDatabase(["supabase_schema", "show_supabase_query_table"], "Show data from Postgres");
        Assert.Empty(tools);
    }
    [Fact]
    public void DefaultAllowlistIsTheEightProductToolsAndExcludesCSharpTools()
    {
        var selected = AgentToolPolicy.SelectTools(developerMode: false,
            ["csharp_contracts", "csharp_write", "csharp_run"]);
        Assert.Equal(
            ["table_read", "table_refine", "show_form", "show_view",
             "supabase_schema", "show_supabase_query_table", "postgres_schema", "show_postgres_query_table"],
            selected);
        Assert.Equal(8, AgentToolPolicy.MaxDefaultTools);
    }

    [Fact]
    public void RelevantAppToolsJoinTheCoreAndNoneOfThemExceedTheCap()
    {
        var selected = AgentToolPolicy.SelectTools(developerMode: false, [],
            ["propose_app", "run_leadgenerator"]);
        Assert.True(selected.Count <= AgentToolPolicy.MaxDefaultTools);
        Assert.DoesNotContain("find_capability", selected);
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
            ["propose_app", "run_leadgenerator", "plan_background_removal", "run_background_removal", "extra_app_tool"]);
        Assert.Equal(AgentToolPolicy.MaxDefaultTools, selected.Count);
        Assert.Equal(selected.Count, selected.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ExplicitDeveloperModeIncludesCSharpTools()
    {
        string[] developerTools = ["csharp_contracts", "csharp_write", "csharp_run"];
        var selected = AgentToolPolicy.SelectTools(developerMode: true, developerTools);
        Assert.Equal(AgentToolPolicy.ProductTools.Concat(developerTools), selected);
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
