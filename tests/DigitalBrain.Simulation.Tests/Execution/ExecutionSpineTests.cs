using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using DigitalBrain.SmartPrompt;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class ExecutionSimulationFixture : IAsyncLifetime
{
    private BrainSimulation? _sim;

    public BrainSimulation Sim => _sim ?? throw new InvalidOperationException("Simulation has not started.");

    public async ValueTask InitializeAsync()
        => _sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest(
                [
                    typeof(ExecutionModule),
                    typeof(GoogleModule),
                    typeof(SalesforceModule),
                    typeof(DigitalBrain.Time.TimeModule),
                ]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            // The Smart Prompt module owns the web search capability; the spine only needs the
            // capability and its fake, not the whole module.
            ConfigureSilo = silo => silo.Services
                .AddSingleton<ICapabilityHandler, TestEchoCapabilityHandler>()
                .AddSingleton<ICapabilityHandler, WebSearchHandler>()
                .AddSingleton<IWebSearch, FakeWebSearch>(),
        });

    public async ValueTask DisposeAsync()
    {
        if (_sim is not null)
        {
            await _sim.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ExecutionSimulationCollection : ICollectionFixture<ExecutionSimulationFixture>
{
    public const string Name = "execution-simulation";
}

[Collection(ExecutionSimulationCollection.Name)]
public sealed class ExecutionSpineTests(ExecutionSimulationFixture fixture)
{
    [Theory]
    [InlineData(ExecutionDriverKind.Agent, "test.echo", "test.echo.v1", "pong")]
    [InlineData(ExecutionDriverKind.Agent, "gmail.search", "gmail.search.v1", "New Customer")]
    [InlineData(ExecutionDriverKind.Script, "test.echo", "test.echo.v1", "pong")]
    [InlineData(ExecutionDriverKind.Script, "gmail.search", "gmail.search.v1", "New Customer")]
    public async Task StartExecution_invokes_allow_listed_capability(
        ExecutionDriverKind driver,
        string capability,
        string schemaHash,
        string payloadSnippet)
    {
        var brain = fixture.Sim.Brain;
        var executionId = ExecutionId.New();

        var execution = await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            executionId,
            new SmartPromptWorkload(Guid.NewGuid(), Guid.NewGuid(), "hi"),
            driver,
            [CapabilityId.Parse(capability)],
            cancellationToken: TestContext.Current.CancellationToken);

        var executionContext = brain.GetEntity<IExecutionContext>(executionId.ToString());
        var entry = await executionContext.Query(new ContextQuery(new ContextPath(capability)));

        Assert.NotNull(entry);
        Assert.Equal(schemaHash, entry!.SchemaHash);
        Assert.Contains(payloadSnippet, entry.PayloadJson, StringComparison.Ordinal);

        var projection = await execution.Read();
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.Equal(executionId, projection.ExecutionId);
        Assert.Equal(driver, projection.Driver);
        Assert.IsType<SmartPromptWorkload>(projection.Workload);
    }

    [Fact]
    public async Task StartExecution_admits_related_execution_context_slots()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;

        var firstId = ExecutionId.New();
        await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            firstId,
            new SmartPromptWorkload(Guid.NewGuid(), Guid.NewGuid(), "search"),
            ExecutionDriverKind.Agent,
            [CapabilityId.Parse("gmail.search")],
            cancellationToken: cancellationToken);

        var secondId = ExecutionId.New();
        await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            secondId,
            new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "follow-up"),
            ExecutionDriverKind.Agent,
            grants: [],
            relatedExecutions: [firstId],
            cancellationToken: cancellationToken);

        var secondContext = brain.GetEntity<IExecutionContext>(secondId.ToString());
        var entry = await secondContext.Query(new ContextQuery(new ContextPath("gmail.search")));

        Assert.NotNull(entry);
        Assert.Equal("gmail.search.v1", entry!.SchemaHash);
        Assert.Contains("New Customer", entry.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartExecution_TeamWorkload_runs_researcher_then_closer_and_writes_team_trace()
    {
        var brain = fixture.Sim.Brain;
        var executionId = ExecutionId.New();

        var execution = await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            executionId,
            new TeamWorkload(
                Goal: "Research lead then close in Salesforce",
                ParticipantNames: ["researcher", "closer"]),
            ExecutionDriverKind.Team,
            [
                CapabilityId.Parse("gmail.search"),
                CapabilityId.Parse("websearch.company"),
                CapabilityId.Parse("salesforce.upsert"),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var executionContext = brain.GetEntity<IExecutionContext>(executionId.ToString());
        var gmail = await executionContext.Query(new ContextQuery(new ContextPath("gmail.search")));
        var websearch = await executionContext.Query(new ContextQuery(new ContextPath("websearch.company")));
        var salesforce = await executionContext.Query(new ContextQuery(new ContextPath("salesforce.upsert")));
        var teamTrace = await executionContext.Query(new ContextQuery(new ContextPath("team.trace")));

        Assert.NotNull(gmail);
        Assert.Equal("gmail.search.v1", gmail!.SchemaHash);
        Assert.NotNull(websearch);
        Assert.Equal("websearch.company.v1", websearch!.SchemaHash);
        Assert.NotNull(salesforce);
        Assert.Equal("salesforce.upsert.v1", salesforce!.SchemaHash);
        Assert.NotNull(teamTrace);
        Assert.Equal("team.trace.v1", teamTrace!.SchemaHash);
        Assert.Contains("researcher", teamTrace.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("closer", teamTrace.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("gmail.search", teamTrace.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("websearch.company", teamTrace.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("salesforce.upsert", teamTrace.PayloadJson, StringComparison.Ordinal);

        var projection = await execution.Read();
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.Equal(ExecutionDriverKind.Team, projection.Driver);
        Assert.IsType<TeamWorkload>(projection.Workload);
    }
}
