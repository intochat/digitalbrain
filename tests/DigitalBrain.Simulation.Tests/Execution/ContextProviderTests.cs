using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Execution;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

[Collection(ExecutionSimulationCollection.Name)]
public sealed class ContextProviderTests(ExecutionSimulationFixture fixture)
{
    [Fact]
    public async Task Preference_rule_is_seeded_into_prompt_blocks_and_context()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;

        var preferences = brain.GetEntity<IPreferenceStore>(IPreferenceStore.DefaultInstanceName);
        await preferences.AddRule("tone", "Be concise and direct.");

        var executionId = ExecutionId.New();
        var name = executionId.ToString();
        var exec = brain.GetGrainProxy<IExecution>(name);

        await exec.HandleAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "hi"),
                ExecutionDriverKind.Agent,
                Grants: []),
            cancellationToken);

        await JournalWait.ForAsync(
            brain,
            NeuronId.For<IExecution>(brain.Owner, name),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is ExecutionLifecycle
            {
                Status: ExecutionStatus.Completed
            });

        var projection = await exec.Read();
        Assert.NotNull(projection.PromptBlocks);
        Assert.Contains(
            projection.PromptBlocks!,
            block => block.Contains("Be concise and direct.", StringComparison.Ordinal));

        var ctx = brain.GetEntity<IExecutionContext>(name);
        var entry = await ctx.Query(new ContextQuery(new ContextPath("preferences.rules")));
        Assert.NotNull(entry);
        Assert.Equal("preferences.rules.v1", entry!.SchemaHash);
        Assert.Contains("Be concise and direct.", entry.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explain_why_writes_explain_trace_context()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;

        var preferences = brain.GetEntity<IPreferenceStore>(IPreferenceStore.DefaultInstanceName);
        await preferences.AddRule("privacy", "Never share personal emails.");

        var executionId = ExecutionId.New();
        var name = executionId.ToString();
        var exec = brain.GetGrainProxy<IExecution>(name);

        await exec.HandleAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "why?"),
                ExecutionDriverKind.Agent,
                [CapabilityId.Parse("explain.why")]),
            cancellationToken);

        await JournalWait.ForAsync(
            brain,
            NeuronId.For<IExecution>(brain.Owner, name),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is ExecutionLifecycle
            {
                Status: ExecutionStatus.Completed
            });

        var ctx = brain.GetEntity<IExecutionContext>(name);
        var entry = await ctx.Query(new ContextQuery(new ContextPath("explain.trace")));
        Assert.NotNull(entry);
        Assert.Equal("explain.trace.v1", entry!.SchemaHash);
        Assert.Contains(executionId.ToString(), entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("ChatTurnWorkload", entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("preferences.rules", entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("Never share personal emails.", entry.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("Based on active execution context and preferences.", entry.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Related_executions_add_prompt_block()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;

        var relatedId = ExecutionId.New();
        var executionId = ExecutionId.New();
        var name = executionId.ToString();
        var exec = brain.GetGrainProxy<IExecution>(name);

        await exec.HandleAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                new SmartPromptWorkload(Guid.NewGuid(), Guid.NewGuid(), "follow up"),
                ExecutionDriverKind.Script,
                Grants: [],
                RelatedExecutions: [relatedId]),
            cancellationToken);

        await JournalWait.ForAsync(
            brain,
            NeuronId.For<IExecution>(brain.Owner, name),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is ExecutionLifecycle
            {
                Status: ExecutionStatus.Completed
            });

        var projection = await exec.Read();
        Assert.NotNull(projection.PromptBlocks);
        Assert.Contains(
            projection.PromptBlocks!,
            block => block.Contains(relatedId.ToString(), StringComparison.Ordinal));
    }
}
