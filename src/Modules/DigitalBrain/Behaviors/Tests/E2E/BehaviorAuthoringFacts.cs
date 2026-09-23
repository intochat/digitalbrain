using System.Reflection;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit;

namespace IntoChat.Tests;

public sealed class BehaviorAuthoringFacts
{
    [Fact]
    public async Task ChatDraftSaveRegistersInTheSameWorkspaceCatalog()
    {
        var registry = new BehaviorCatalogStore(new InMemoryDocumentStore<BehaviorCatalogIndex>(), new InMemoryDocumentStore<BehaviorCatalogDocument>());
        await using var fixture = new Fixture(registry);
        var tools = new BehaviorAgentTools(fixture.Tools).Create(() => new("workspace-one", "run", "call"));
        await tools.Single(x => x.Name == "code_draft_save").InvokeAsync(new AIFunctionArguments
        {
            ["id"] = "timer",
            ["request"] = new SaveCodeDraft(0, Guid.NewGuid(), "source", "tests", []),
        }, TestContext.Current.CancellationToken);
        Assert.Equal("timer", Assert.Single(await registry.List("workspace-one", TestContext.Current.CancellationToken)).Id);
        Assert.Empty(await registry.List("workspace-two", TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task CheckToolWaitsForPendingValidationToSettle()
    {
        await using var fixture = new Fixture { PendingReads = 2 };
        var result = await fixture.Tools.ForScope("trusted").ReadCheck("timer", Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(CodeCheckStatus.Passed, result.Status);
        Assert.NotNull(result.Artifact);
    }

    [Fact]
    public async Task AgentCanRepairMalformedToolArgumentsAndFinishTheConversation()
    {
        await using var fixture = new Fixture();
        await using var services = new ServiceCollection().AddSingleton<IChatClient, RepairClient>()
            .AddSingleton<IAgentToolFactory>(new BehaviorAgentTools(fixture.Tools)).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(
            new("agent", "run", "trusted", [], "read contracts", null, ToolNames: ["code_contracts"]), TestContext.Current.CancellationToken))
        { events.Add(item); }
        Assert.Empty(events.OfType<AgentTurnEvent.Failed>());
        Assert.Equal(2, events.OfType<AgentTurnEvent.ToolCompleted>().Count());
        Assert.Single(events.OfType<AgentTurnEvent.Finished>());
        Assert.Equal("Contracts inspected after repair.", Assert.Single(events.OfType<AgentTurnEvent.Text>()).Content);
    }

    private sealed class RepairClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
            if (results.Length == 1)
            { Assert.True(JsonSerializer.SerializeToElement(results[0].Result).GetProperty("isError").GetBoolean()); }
            if (results.Length == 2)
            {
                Assert.True(JsonSerializer.SerializeToElement(results[1].Result).TryGetProperty("environmentHash", out _));
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Contracts inspected after repair.")));
            }
            string[] modules = results.Length == 0 ? ["not-installed"] : [];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-" + results.Length, "code_contracts", new Dictionary<string, object?>
                    { ["modules"] = modules })])));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task InvalidContractSelectionReturnsAnActionableToolResult()
    {
        await using var fixture = new Fixture();
        var tools = new BehaviorAgentTools(fixture.Tools).Create(() => new("trusted", "run", "call"));
        var result = await tools.Single(x => x.Name == "code_contracts").InvokeAsync(
            new AIFunctionArguments { ["modules"] = new[] { "missing" } }, TestContext.Current.CancellationToken);
        var json = JsonSerializer.SerializeToElement(result);
        Assert.True(json.GetProperty("isError").GetBoolean());
        Assert.Contains("missing", json.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedOperationIdentityReturnsRepairableToolResult()
    {
        await using var fixture = new Fixture();
        var tools = new BehaviorAgentTools(fixture.Tools).Create(() => new("trusted", "run", "call"));
        var result = await tools.Single(x => x.Name == "behavior_start").InvokeAsync(
            new AIFunctionArguments { ["id"] = "timer", ["request"] = JsonSerializer.SerializeToElement(new { expectedRevision = 0, operationId = "start-timer" }) },
            TestContext.Current.CancellationToken);
        var json = JsonSerializer.SerializeToElement(result);
        Assert.True(json.GetProperty("isError").GetBoolean());
        Assert.Contains("Guid", json.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(fixture.Keys);
    }

    [Fact]
    public async Task FailedCandidateIsRepairedAndDraftOnlyDoesNotDeploy()
    {
        await using var fixture = new Fixture();
        fixture.FailFirstCheck = true;
        var result = await fixture.Service.AuthorAsync("trusted", new("timer", "Write timer behavior"), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Artifact);
        Assert.Equal("Validated", result.Status);
        Assert.Equal(2, fixture.Prompts.Count);
        Assert.Contains("compiler diagnostic", fixture.Prompts[1], StringComparison.Ordinal);
        Assert.Equal(2, result.Revision);
        Assert.DoesNotContain(fixture.Keys, x => x.Type.Name == "IBehaviorProgram");
    }

    [Fact]
    public async Task RefusalStopsAfterThreeCandidatesWithoutExecutingCode()
    {
        await using var fixture = new Fixture { Reply = "I cannot generate that behavior." };
        var result = await fixture.Service.AuthorAsync("trusted", new("timer", "Write timer behavior"), TestContext.Current.CancellationToken);
        Assert.Equal("Failed", result.Status);
        Assert.Null(result.Artifact);
        Assert.Equal(3, fixture.Prompts.Count);
        Assert.Equal(0, result.Revision);
    }

    [Fact]
    public async Task CancellationCancelsTheOutstandingCheck()
    {
        await using var fixture = new Fixture { Pending = true };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = fixture.Service.AuthorAsync("trusted", new("timer", "Write timer behavior"), cancel.Token);
        await fixture.CheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(fixture.Cancelled);
    }

    [Fact]
    public async Task HostPolicyRejectsActivationBeforeCallingTheModel()
    {
        await using var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AuthorAsync("trusted", new("timer", "Write timer behavior", true), TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Prompts);
    }

    [Fact]
    public async Task NativeAndMcpToolsUseTheSameScopeAndResults()
    {
        await using var fixture = new Fixture();
        var scope = "first";
        var native = new BehaviorAgentTools(fixture.Tools).Create(() => new(scope, "run", "call"));
        Assert.Equal(BehaviorAgentTools.Names.Order(), native.Select(x => x.Name).Order());
        scope = "trusted";
        var result = await native.Single(x => x.Name == "code_draft_read").InvokeAsync(new AIFunctionArguments { ["id"] = "timer" }, TestContext.Current.CancellationToken);
        var mcp = await fixture.Tools.ForScope(scope).ReadDraft("timer", TestContext.Current.CancellationToken);
        var decoded = JsonSerializer.SerializeToElement(result).Deserialize<CodeDraftSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equivalent(mcp, decoded);
        Assert.All(fixture.Keys, x => Assert.Equal(BehaviorToolScope.Key(scope, "timer"), x.Id));
    }

    private sealed class Fixture : IDigitalBrain
    {
        private long _revision;
        private int _checks;
        public List<string> Prompts { get; } = [];
        public List<(Type Type, string Id)> Keys { get; } = [];
        public string Reply { get; init; } = "{\"source\":\"source\",\"tests\":\"tests\",\"moduleIds\":[]}";
        public bool FailFirstCheck { get; set; }
        public bool Pending { get; init; }
        public int PendingReads { get; set; }
        public bool Cancelled { get; private set; }
        public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BehaviorAuthoringService Service { get; }
        public BehaviorToolService Tools { get; }
        public Fixture(BehaviorCatalogStore? registry = null)
        {
            var catalog = new ContractCatalog(Options.Create(new CodeExecutionOptions()));
            var options = Options.Create(new BehaviorAuthoringOptions());
            Tools = new(this, catalog, options, registry);
            Service = new(this, Tools, catalog, options);
        }
        public T Get<T>(string id) where T : class, IGrainWithStringKey
        {
            Keys.Add((typeof(T), id));
            var proxy = DispatchProxy.Create<T, MethodProxy>();
            ((MethodProxy)(object)proxy).Handler = (method, args) => method.Name switch
            {
                "Read" => Task.FromResult(new CodeDraftSnapshot(_revision, "", "", [], null)),
                "Configure" => Configure((AgentDefinition)args[0]!),
                "GetRichResponse" => Respond((string)args[0]!),
                "Save" => Save((SaveCodeDraft)args[0]!),
                "Check" => Check((CheckCodeDraft)args[0]!),
                "ReadCheck" => Task.FromResult(new CodeCheckSnapshot((Guid)args[0]!, 1,
                    PendingReads-- > 0 ? CodeCheckStatus.Building : CodeCheckStatus.Passed, [], null, DateTimeOffset.UtcNow, null, new("artifact", "source", "env"))),
                "CancelCheck" => Cancel(),
                _ => throw new NotSupportedException(method.Name),
            };
            return proxy;
        }
        private static Task Configure(AgentDefinition definition) { Assert.True(definition.Instructions.Length <= 32000); return Task.CompletedTask; }
        private Task<AgentResponse> Respond(string prompt) { Prompts.Add(prompt); return Task.FromResult(new AgentResponse(Guid.NewGuid().ToString(), Reply, [], null)); }
        private Task<CodeDraftSnapshot> Save(SaveCodeDraft request)
        {
            Assert.Equal(_revision, request.ExpectedRevision);
            return Task.FromResult(new CodeDraftSnapshot(++_revision, request.Source, request.Tests, request.ModuleIds, null));
        }
        private Task<CodeCheckSnapshot> Check(CheckCodeDraft request)
        {
            _checks++;
            CheckStarted.TrySetResult();
            var fail = FailFirstCheck && _checks == 1;
            return Task.FromResult(new CodeCheckSnapshot(request.OperationId, request.Revision,
                Pending ? CodeCheckStatus.Building : fail ? CodeCheckStatus.Failed : CodeCheckStatus.Passed,
                fail ? [new("compile", "error", "compiler diagnostic")] : [], null, DateTimeOffset.UtcNow, null,
                Pending || fail ? null : new("artifact", "source", "env")));
        }
        private Task Cancel() { Cancelled = true; return Task.CompletedTask; }
        public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    public class MethodProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args ?? []);
    }
}