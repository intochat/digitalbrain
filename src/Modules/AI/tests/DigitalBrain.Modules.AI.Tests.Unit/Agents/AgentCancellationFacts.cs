using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class AgentCancellationFacts
{
    [Fact]
    public async Task CancelInterleavesWithInferenceAndDoesNotCommitAnIncompleteTurn()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new WaitingProvider();
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => s.Services.AddSingleton<IChatClient>(provider.Client)).StartAsync(ct);
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
            .ConfigureSilo(s => s.Services.AddSingleton<IChatClient>(new WaitingProvider(fail: true).Client)).StartAsync(ct);
        var agent = brain.Get<IAgent>("failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.GetResponse("fail", ct));
        Assert.Empty(await agent.GetHistory(ct));
        Assert.Equal(AgentRunStatus.Failed, (await agent.GetState(ct)).LastRun!.Status);
    }

    private sealed class WaitingProvider(bool fail = false)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StubChatClient Client => new(Respond, Stream);

        private async Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (fail) { throw new IOException("Provider unavailable"); }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }

        private async IAsyncEnumerable<ChatResponseUpdate> Stream(IReadOnlyList<ChatMessage> messages, ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new(ChatRole.Assistant, "partial");
            await Respond(messages, options, cancellationToken);
        }
    }
}
