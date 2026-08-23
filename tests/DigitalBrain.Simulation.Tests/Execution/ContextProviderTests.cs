using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;
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
            ExecutionDriverKind.Agent,
            grants: [],
            cancellationToken: TestContext.Current.CancellationToken);

        var projection = await execution.Read();
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
    public async Task Explain_why_writes_explain_trace_context()
    {
        var brain = fixture.Sim.Brain;

        var preferences = brain.GetEntity<IPreferenceStore>(IPreferenceStore.DefaultInstanceName);
        await preferences.AddRule("privacy", "Never share personal emails.");

        var executionId = ExecutionId.New();
        await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            executionId,
            new SmartPromptWorkload(Guid.NewGuid(), Guid.NewGuid(), "why?"),
            ExecutionDriverKind.Agent,
            [CapabilityId.Parse("explain.why")],
            cancellationToken: TestContext.Current.CancellationToken);

        var executionContext = brain.GetEntity<IExecutionContext>(executionId.ToString());
        var entry = await executionContext.Query(new ContextQuery(new ContextPath("explain.trace")));
        Assert.NotNull(entry);
        Assert.Equal("explain.trace.v1", entry!.SchemaHash);
        Assert.Contains(executionId.ToString(), entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("SmartPromptWorkload", entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("preferences.rules", entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("Never share personal emails.", entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("Based on active execution context and preferences.", entry.PayloadJson, StringComparison.Ordinal);
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
            new SmartPromptWorkload(Guid.NewGuid(), Guid.NewGuid(), "follow up"),
            ExecutionDriverKind.Script,
            grants: [],
            relatedExecutions: [relatedId],
            cancellationToken: TestContext.Current.CancellationToken);

        var projection = await execution.Read();
        Assert.NotNull(projection.PromptBlocks);
        Assert.Contains(
            projection.PromptBlocks!,
            block => block.Contains(relatedId.ToString(), StringComparison.Ordinal));
    }
}
