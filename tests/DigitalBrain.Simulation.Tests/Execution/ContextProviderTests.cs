using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

[Collection(ExecutionSimulationCollection.Name)]
public sealed class ContextProviderTests(ExecutionSimulationFixture fixture)
{
    [Fact]
    public async Task StartExecution_seeds_the_user_turn_into_prompt_and_context()
    {
        var brain = fixture.Sim.Brain;
        var executionId = ExecutionId.New();
        var turnId = Guid.NewGuid();
        var execution = await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            executionId,
            new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), turnId, "hello there"),
            cancellationToken: TestContext.Current.CancellationToken);

        var projection = await execution.RequestAsync(
            new ReadExecution(),
            TestContext.Current.CancellationToken);
        Assert.NotNull(projection.PromptBlocks);
        Assert.Contains(
            projection.PromptBlocks!,
            block => block.Contains("hello there", StringComparison.Ordinal));

        var executionContext = brain.GetEntity<IExecutionContext>(executionId.ToString());
        var entry = await executionContext.Query(new ContextQuery(new ContextPath($"chat.turn.{turnId:N}")));
        Assert.NotNull(entry);
        Assert.Equal("chat.turn.v1", entry!.SchemaHash);
        Assert.Contains("hello there", entry.PayloadJson, StringComparison.Ordinal);
    }
}
