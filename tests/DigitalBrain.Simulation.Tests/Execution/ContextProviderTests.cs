using DigitalBrain.Execution;
using DigitalBrain.Abstractions.Identity;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

[Collection(ExecutionSimulationCollection.Name)]
public sealed class ContextProviderTests(ExecutionSimulationFixture fixture)
{
    [Fact]
    public async Task Preference_rule_is_seeded_into_prompt_blocks_and_context()
    {
        var brain = fixture.Sim.Brain;

        var preferences = brain.GetEntity<IPreferenceStore>(IPreferenceStore.DefaultInstanceName);
        await preferences.AddRule("tone", "Be concise and direct.");

        var executionId = ExecutionId.New();
        var execution = await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            executionId,
            new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "hi"),
            cancellationToken: TestContext.Current.CancellationToken);

        var projection = await execution.RequestAsync(
            new ReadExecution(),
            TestContext.Current.CancellationToken);
        Assert.NotNull(projection.PromptBlocks);
        Assert.Contains(
            projection.PromptBlocks!,
            block => block.Contains("Be concise and direct.", StringComparison.Ordinal));

        var executionContext = brain.GetEntity<IExecutionContext>(executionId.ToString());
        var entry = await executionContext.Query(new ContextQuery(new ContextPath("preferences.rules")));
        Assert.NotNull(entry);
        Assert.Equal("preferences.rules.v1", entry!.SchemaHash);
        Assert.Contains("Be concise and direct.", entry.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Related_executions_add_prompt_block()
    {
        var brain = fixture.Sim.Brain;

        var relatedId = ExecutionId.New();
        var executionId = ExecutionId.New();
        var execution = await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            executionId,
            new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "follow up"),
            relatedExecutions: [relatedId],
            cancellationToken: TestContext.Current.CancellationToken);

        var projection = await execution.RequestAsync(
            new ReadExecution(),
            TestContext.Current.CancellationToken);
        Assert.NotNull(projection.PromptBlocks);
        Assert.Contains(
            projection.PromptBlocks!,
            block => block.Contains(relatedId.ToString(), StringComparison.Ordinal));
    }
}
