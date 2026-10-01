using DigitalBrain.Contracts;
using DigitalBrain.AI.Metering;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class AgentExecutionFacts
{
    [Fact]
    public async Task MeteringStorageFailureDoesNotInvalidateASuccessfulExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new FailingUsageSink();
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(StubChatClient.Replying("kept answer"))
                .AddSingleton<IIntentUsageSink>(sink)).StartAsync(ct);
        var events = new List<AgentTurnEvent>();
        await foreach (var item in brain.Get<IAgentExecution>("metering-failure").Run(
            new("agent", "run", "scope", [], "question", null), "intent", ct))
        { events.Add(item); }
        Assert.Equal(1, sink.FlushCalls);
        Assert.Equal("kept answer", Assert.Single(events.OfType<AgentTurnEvent.Text>()).Content);
        Assert.Single(events.OfType<AgentTurnEvent.Finished>());
        Assert.Empty(events.OfType<AgentTurnEvent.Failed>());
    }

    private sealed class FailingUsageSink : IIntentUsageSink
    {
        public int FlushCalls { get; private set; }
        public Task RecordAsync(string intentId, TokenUsageEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task FlushAsync(IntentContext intent, CancellationToken cancellationToken = default)
        {
            FlushCalls++;
            throw new IOException("Usage storage unavailable");
        }
    }

    [Fact]
    public async Task ExecutionContractStreamsScriptedModelOutputAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IChatClient>(StubChatClient.Replying("answer"))).StartAsync(ct);
        var execution = brain.Get<IAgentExecution>("execution");
        var events = new List<AgentTurnEvent>();
        await foreach (var item in execution.Run(new("agent", "run", "scope", [], "question", null), "intent", ct))
        { events.Add(item); }
        Assert.Equal("answer", Assert.Single(events.OfType<AgentTurnEvent.Text>()).Content);
        Assert.Single(events.OfType<AgentTurnEvent.Finished>());
        Assert.Empty(events.OfType<AgentTurnEvent.Failed>());
        await brain.DeactivateAsync(execution, ct);
        Assert.Empty((await execution.Models()).Models);
        await Assert.ThrowsAsync<ArgumentException>(() => execution.SelectModel("profile:missing"));
    }

    [Fact]
    public async Task ExecutionWithoutAProviderReturnsFailureInsteadOfSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        var events = new List<AgentTurnEvent>();
        await foreach (var item in brain.Get<IAgentExecution>("unavailable").Run(new("agent", "run", "scope", [], "question", null), "intent", ct))
        { events.Add(item); }
        Assert.Single(events.OfType<AgentTurnEvent.Failed>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
        Assert.False((await brain.Get<IAgentExecution>("unavailable").Models()).Automatic.Available);
    }
}
