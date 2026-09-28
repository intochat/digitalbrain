using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentCancellationFacts
{
    [Fact]
    public async Task CancelInterleavesWithInferenceAndDoesNotCommitAnIncompleteTurn()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new WaitingClient();
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => s.Services.AddSingleton<IChatClient>(provider)).StartAsync(ct);
        var agent = brain.Get<IAgent>("cancel");
        var response = agent.GetResponse("wait", ct);
        await provider.Entered.Task.WaitAsync(ct);
        Assert.Equal(AgentRunStatus.Running, (await agent.GetState(ct)).LastRun!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ClearHistory(ct));
        await agent.Cancel(ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
        Assert.Empty(await agent.GetHistory(ct));
        Assert.Equal(AgentRunStatus.Cancelled, (await agent.GetState(ct)).LastRun!.Status);
    }

    [Fact]
    public async Task ProviderFailureIsFailedAndNeverCommitsHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => s.Services.AddSingleton<IChatClient>(new WaitingClient(fail: true))).StartAsync(ct);
        var agent = brain.Get<IAgent>("failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.GetResponse("fail", ct));
        Assert.Empty(await agent.GetHistory(ct));
        Assert.Equal(AgentRunStatus.Failed, (await agent.GetState(ct)).LastRun!.Status);
    }

    private sealed class WaitingClient(bool fail = false) : IChatClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (fail) { throw new IOException("Provider unavailable"); }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.CompletedTask; yield return new(ChatRole.Assistant, "partial"); await GetResponseAsync(messages, options, cancellationToken); }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}