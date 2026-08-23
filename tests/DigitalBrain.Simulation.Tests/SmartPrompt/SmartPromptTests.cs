using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Execution;
using DigitalBrain.SmartPrompt;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

[Collection(SimulationCollection.Name)]
public sealed class SmartPromptTests(SimulationFixture fixture)
{
    [Fact]
    public async Task Save_and_RunSmartPrompt_starts_execution_with_facade_grants()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;
        var promptName = fixture.Sim.UniqueId("prompt");

        var prompt = brain.GetEntity<ISmartPrompt>(promptName);
        await prompt.Save(new SmartPromptDocument(
            Title: "Pull new leads",
            BodyText: "Search Gmail for new customers, enrich via web search, upsert Salesforce.",
            Bindings:
            [
                new SmartPromptBinding("gmail", Label: "inbox", Account: null),
                new SmartPromptBinding("salesforce", Label: "lead", Account: null),
                new SmartPromptBinding("websearch", Label: "company", Account: null),
                new SmartPromptBinding("schedule", Label: "15m", Account: null),
            ],
            Enabled: true));

        var saved = await prompt.Read();
        Assert.NotNull(saved);
        Assert.True(saved!.Document.Enabled);
        Assert.NotNull(saved.ActiveRevisionId);
        Assert.Equal(4, saved.Document.Bindings.Count);

        var commandId = CommandId.New();
        await brain.FireAsync<ISmartPromptRunner>(
            promptName,
            new RunSmartPrompt(commandId, promptName, OfferChat: null),
            cancellationToken);

        var started = await JournalWait.ForAsync(
            brain,
            NeuronId.For<ISmartPromptRunner>(brain.Owner, promptName),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is SmartPromptRunStarted run
                && run.CommandId == commandId
                && run.PromptName == promptName);

        var runStarted = Assert.IsType<SmartPromptRunStarted>(started.Synapse);
        var executionName = runStarted.ExecutionId.ToString();

        await JournalWait.ForAsync(
            brain,
            NeuronId.For<IExecution>(brain.Owner, executionName),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is ExecutionLifecycle
            {
                Status: ExecutionStatus.Completed
            });

        var context = brain.GetEntity<IExecutionContext>(executionName);
        var gmail = await context.Query(new ContextQuery(new ContextPath("gmail.search")));
        var salesforce = await context.Query(new ContextQuery(new ContextPath("salesforce.upsert")));
        var websearch = await context.Query(new ContextQuery(new ContextPath("websearch.company")));

        Assert.NotNull(gmail);
        Assert.Equal("gmail.search.v1", gmail!.SchemaHash);
        Assert.NotNull(salesforce);
        Assert.Equal("salesforce.upsert.v1", salesforce!.SchemaHash);
        Assert.NotNull(websearch);
        Assert.Equal("websearch.company.v1", websearch!.SchemaHash);

        var projection = await brain.GetGrainProxy<IExecution>(executionName).Read();
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.IsType<SmartPromptWorkload>(projection.Workload);
    }

    [Fact]
    public async Task Disabled_prompt_refuses_run()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;
        var promptName = fixture.Sim.UniqueId("disabled-prompt");

        var prompt = brain.GetEntity<ISmartPrompt>(promptName);
        await prompt.Save(new SmartPromptDocument(
            Title: "Off",
            BodyText: "Should not run.",
            Bindings: [new SmartPromptBinding("gmail", null, null)],
            Enabled: false));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(async () =>
            await brain.GetGrainProxy<ISmartPromptRunner>(promptName).HandleAsync(
                new RunSmartPrompt(CommandId.New(), promptName, OfferChat: null),
                cancellationToken));
    }
}
