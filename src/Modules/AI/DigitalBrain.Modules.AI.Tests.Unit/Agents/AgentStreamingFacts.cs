using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Agents.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentStreamingFacts
{
    [Fact]
    public async Task ProviderFailureIsDeliveredEvenWhenTheOutputBufferIsFull()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var client = new BurstClient();
        using var services = new ServiceCollection().AddSingleton<IChatClient>(client).BuildServiceProvider();
        await using var stream = new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "hello", null, Streaming: true), deadline.Token).GetAsyncEnumerator(deadline.Token);
        Assert.True(await stream.MoveNextAsync());
        Assert.IsType<AgentTurnEvent.Started>(stream.Current);
        await client.Failing.Task.WaitAsync(deadline.Token);
        var failures = new List<AgentTurnEvent.Failed>();
        while (await stream.MoveNextAsync())
        {
            if (stream.Current is AgentTurnEvent.Failed failure) { failures.Add(failure); }
            Assert.IsNotType<AgentTurnEvent.Finished>(stream.Current);
        }
        Assert.Contains("Provider stopped", Assert.Single(failures).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbandonedStreamingCancelsProviderAndLeavesHistoryUnchanged()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        var provider = new WaitingStreamClient();
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => s.Services.AddSingleton<IChatClient>(provider)).StartAsync(ct);
        var agent = brain.Get<IAgent>("abandoned");
        await using var changes = await brain.Observe<AgentRunChanged>(agent, ct);
        await using (var response = agent.GetResponseStream("hello", ct).GetAsyncEnumerator(ct))
        {
            Assert.True(await response.MoveNextAsync());
            Assert.Equal("partial", response.Current);
        }
        await provider.Cancelled.Task.WaitAsync(ct);
        AgentRunChanged changed;
        do { changed = await changes.NextAsync(ct: ct); } while (changed.Run.Status == AgentRunStatus.Running);
        Assert.Equal(AgentRunStatus.Cancelled, changed.Run.Status);
        Assert.Empty(await agent.GetHistory(ct));
    }

    private sealed class BurstClient : IChatClient
    {
        public TaskCompletionSource Failing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < 64; i++) { yield return new(ChatRole.Assistant, "x"); }
            Failing.TrySetResult();
            await Task.CompletedTask;
            throw new IOException("Provider stopped");
        }
        public object? GetService(Type type, object? key = null) => null;
        public void Dispose() { }
    }
    private sealed class WaitingStreamClient : IChatClient
    {
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try { yield return new(ChatRole.Assistant, "partial"); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Cancelled.TrySetResult(); }
        }
        public object? GetService(Type type, object? key = null) => null;
        public void Dispose() { }
    }
}