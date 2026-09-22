using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Testing.Unit;
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
}