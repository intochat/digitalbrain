using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Testing.Unit;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentHistoryFacts
{
    [Fact]
    public async Task ConfigurationAndHistorySurviveReactivationAndClearIsExplicit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(new HistoryClient())).StartAsync(ct);
        var agent = brain.Get<IAgent>("durable");
        await agent.Configure(new() { DisplayName = "Writer", Instructions = "Write carefully" }, 0, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.Configure(new(), 0, ct));
        await agent.GetResponse("remember", ct);
        await brain.DeactivateAsync(agent, ct);
        Assert.Equal("Writer", (await agent.GetMetadata(ct)).DisplayName);
        Assert.Equal(2, (await agent.GetHistory(ct)).Count);
        await agent.ClearHistory(ct);
        Assert.Empty(await agent.GetHistory(ct));
        Assert.Equal(AgentRunStatus.Completed, (await agent.GetState(ct)).LastRun!.Status);
    }

    [Fact]
    public async Task StreamingCommitsHistoryOnlyAfterCompletion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(new HistoryClient())).StartAsync(ct);
        var agent = brain.Get<IAgent>("stream");
        var text = "";
        await foreach (var delta in agent.GetResponseStream("hello", ct)) { text += delta; }
        Assert.Equal("hello", text);
        Assert.Equal(2, (await agent.GetHistory(ct)).Count);
        Assert.Equal(AgentRunStatus.Completed, (await agent.GetState(ct)).LastRun!.Status);
    }

    [Fact]
    public async Task SubsequentQuestionsIncludeCommittedHistoryAndOtherAgentsRemainIsolated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(new HistoryClient()))
            .StartAsync(ct);
        var agent = brain.Get<IAgent>("history");
        Assert.Equal("first", (await agent.Ask(new("first"))).Text);
        Assert.Equal("first|first|second", (await agent.Ask(new("second"))).Text);
        Assert.Equal("other", (await brain.Get<IAgent>("separate").Ask(new("other"))).Text);
    }

    private sealed class HistoryClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Join("|", messages.Select(m => m.Text)))));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, string.Join("|", messages.Select(m => m.Text)));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }

    // The conversation history is capped, not unbounded: at the 4,096-message budget the oldest
    // turns fold into a summary instead of failing the run.
    [Fact]
    public async Task ConversationTurnsAreBoundedToTheMessageBudgetAndSummarized()
    {
        var ct = TestContext.Current.CancellationToken;
        var turns = Enumerable.Range(0, 2048)
            .Select(index => new AgentConversationTurn("run-" + index, "question " + index, "answer " + index, []))
            .ToList();
        var storage = new HistoryStorage
        {
            State = new()
            {
                Json = JsonSerializer.Serialize(new AgentStorage
                {
                    Definition = new() { MaxHistoryMessages = 4096 },
                    Turns = turns,
                    ActiveRun = "overflow",
                    RunInputs = new(StringComparer.Ordinal) { ["overflow"] = "final question" },
                }),
            },
        };
        var agent = new AgentNeuron(storage, new MustNotRun());
        await agent.OnActivateAsync(ct);

        var state = await agent.CompleteConversation(new("overflow", "final question", "final answer", []), ct);

        Assert.Equal(2048, state.Turns.Count);
        Assert.DoesNotContain(state.Turns, turn => turn.RunId == "run-0");
        Assert.Contains(state.Turns, turn => turn.RunId == "overflow");
        Assert.False(string.IsNullOrEmpty(state.Summary));
        Assert.Contains("question 0", state.Summary!, StringComparison.Ordinal);
    }

    // A reused run id must replay only its exact payload, and committed run inputs must not
    // outlive their retained turns, so eviction never leaves a stale id able to mismatch.
    [Fact]
    public async Task ReusedRunIdRequiresTheMatchingPayloadAndEvictionPrunesRunInputs()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new HistoryStorage
        {
            State = new()
            {
                Json = JsonSerializer.Serialize(new AgentStorage { Definition = new() { MaxHistoryMessages = 4 } }),
            },
        };
        var agent = new AgentNeuron(storage, new MustNotRun());
        await agent.OnActivateAsync(ct);

        await agent.BeginConversation(new("r1", "one"), ct);
        await agent.CompleteConversation(new("r1", "one", "answer one", []), ct);
        await agent.BeginConversation(new("r2", "two"), ct);
        await agent.CompleteConversation(new("r2", "two", "answer two", []), ct);

        // A retained run id replays the matching payload and rejects a different one.
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.BeginConversation(new("r2", "different"), ct));
        var replay = await agent.BeginConversation(new("r2", "two"), ct);
        Assert.Equal("answer two", Assert.Single(replay.Turns, turn => turn.RunId == "r2").AssistantText);

        // The third turn evicts r1; committed run inputs never accumulate beyond the active run.
        await agent.BeginConversation(new("r3", "three"), ct);
        await agent.CompleteConversation(new("r3", "three", "answer three", []), ct);
        var persisted = JsonSerializer.Deserialize<AgentStorage>(storage.State.Json)!;
        Assert.Equal(2, persisted.Turns.Count);
        Assert.DoesNotContain(persisted.Turns, turn => turn.RunId == "r1");
        Assert.DoesNotContain("r1", persisted.RunInputs.Keys);
        Assert.True(persisted.RunInputs.Count <= 3, "Committed run inputs must not accumulate.");

        // An evicted id no longer replays; it starts a fresh run instead of returning a stale answer.
        var fresh = await agent.BeginConversation(new("r1", "different"), ct);
        Assert.Equal("r1", fresh.ActiveRunId);
        Assert.DoesNotContain(fresh.Turns, turn => turn.RunId == "r1");
    }

    private sealed class HistoryStorage : IPersistentState<AgentStorageEnvelope>
    {
        public AgentStorageEnvelope State { get; set; } = new();
        public string Etag => "test";
        public bool RecordExists => true;
        public Task ClearStateAsync() => throw new NotSupportedException();
        public Task ReadStateAsync() => Task.CompletedTask;
        public Task WriteStateAsync() => Task.CompletedTask;
    }

    private sealed class MustNotRun : IAgentTurnRunner
    {
        public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("Bounding history must not run the model.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}