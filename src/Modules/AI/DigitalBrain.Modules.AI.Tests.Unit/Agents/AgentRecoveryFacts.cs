using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentRecoveryFacts
{
    [Fact]
    public async Task ActivationMarksUncommittedRunInterruptedWithoutReplayingExternalWork()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new Storage
        {
            State = new()
            {
                Json = JsonSerializer.Serialize(new AgentStorage
                {
                    LastRun = new("interrupted-run", AgentRunStatus.Running, 0, DateTimeOffset.UtcNow),
                }),
            },
        };
        var agent = new AgentNeuron(storage, new MustNotRun());
        await agent.OnActivateAsync(ct);
        Assert.Equal(AgentRunStatus.Interrupted, (await agent.GetState(ct)).LastRun!.Status);
        var persisted = JsonSerializer.Deserialize<AgentStorage>(storage.State.Json)!;
        Assert.Equal(AgentRunStatus.Interrupted, persisted.LastRun!.Status);
        Assert.Contains("outcomes may be unknown", persisted.LastRun.Error, StringComparison.Ordinal);
        Assert.Empty(await agent.GetHistory(ct));
    }

    private sealed class Storage : IPersistentState<AgentStorageEnvelope>
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
            throw new InvalidOperationException("Recovery must not replay a run.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}