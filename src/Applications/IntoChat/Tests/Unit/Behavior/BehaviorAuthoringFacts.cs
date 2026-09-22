using System.Reflection;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Orleans;
using Xunit;

namespace IntoChat.Tests;

public sealed class BehaviorAuthoringFacts
{
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
        public bool Cancelled { get; private set; }
        public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BehaviorAuthoringService Service { get; }
        public BehaviorToolService Tools { get; }
        public Fixture()
        {
            var catalog = new ContractCatalog(Options.Create(new CodeExecutionOptions()));
            var options = Options.Create(new BehaviorAuthoringOptions());
            Tools = new(this, catalog, options);
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
