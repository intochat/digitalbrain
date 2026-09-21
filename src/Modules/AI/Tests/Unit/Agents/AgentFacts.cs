using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Agents.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentFacts
{
    [Fact]
    public async Task AskReturnsTheModelReplyAndPublishesTheSignal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(new FixedChatClient("pong")))
            .StartAsync(ct);

        var agent = brain.Get<IAgent>("assistant");
        await using var replied = await brain.Observe<AgentReplied>(agent, ct);
        var reply = await agent.Ask(new AgentRequest("ping"));

        Assert.Equal("pong", reply.Text);
        var published = await replied.NextAsync(ct: ct);
        Assert.Equal("assistant", published.AgentId);
        Assert.Equal("pong", published.Text);
    }

    private sealed class FixedChatClient(string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
