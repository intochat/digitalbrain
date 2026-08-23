using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using DigitalBrain.Integrations;
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
                    typeof(IntegrationsModule),
                    typeof(DigitalBrain.Time.TimeModule),
                ]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo =>
                silo.Services.AddSingleton<ICapabilityHandler, TestEchoCapabilityHandler>(),
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
    [Fact]
    public async Task StartExecution_creates_context_and_runs_echo_capability()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;
        var executionId = ExecutionId.New();
        var name = executionId.ToString();
        var exec = brain.GetGrainProxy<IExecution>(name);

        await exec.HandleAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "hi"),
                ExecutionDriverKind.Agent,
                [CapabilityId.Parse("test.echo")]),
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
        var entry = await ctx.Query(new ContextQuery(new ContextPath("test.echo")));

        Assert.NotNull(entry);
        Assert.Contains("pong", entry!.PayloadJson, StringComparison.Ordinal);

        var projection = await exec.Read();
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.Equal(executionId, projection.ExecutionId);
    }

    [Fact]
    public async Task FakeGmail_search_writes_schema_shaped_context()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;
        var executionId = ExecutionId.New();
        var name = executionId.ToString();
        var exec = brain.GetGrainProxy<IExecution>(name);

        await exec.HandleAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "hi"),
                ExecutionDriverKind.Agent,
                [CapabilityId.Parse("gmail.search")]),
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
        var entry = await ctx.Query(new ContextQuery(new ContextPath("gmail.search")));

        Assert.NotNull(entry);
        Assert.Equal("gmail.search.v1", entry!.SchemaHash);
        Assert.Contains("New Customer", entry.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartExecution_admits_related_execution_context_slots()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;

        var firstId = ExecutionId.New();
        var firstName = firstId.ToString();
        var first = brain.GetGrainProxy<IExecution>(firstName);

        await first.HandleAsync(
            new StartExecution(
                CommandId.New(),
                firstId,
                new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "search"),
                ExecutionDriverKind.Agent,
                [CapabilityId.Parse("gmail.search")]),
            cancellationToken);

        await JournalWait.ForAsync(
            brain,
            NeuronId.For<IExecution>(brain.Owner, firstName),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is ExecutionLifecycle
            {
                Status: ExecutionStatus.Completed
            });

        var secondId = ExecutionId.New();
        var secondName = secondId.ToString();
        var second = brain.GetGrainProxy<IExecution>(secondName);

        await second.HandleAsync(
            new StartExecution(
                CommandId.New(),
                secondId,
                new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "follow-up"),
                ExecutionDriverKind.Agent,
                Grants: [],
                RelatedExecutions: [firstId]),
            cancellationToken);

        await JournalWait.ForAsync(
            brain,
            NeuronId.For<IExecution>(brain.Owner, secondName),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is ExecutionLifecycle
            {
                Status: ExecutionStatus.Completed
            });

        var secondContext = brain.GetEntity<IExecutionContext>(secondName);
        var entry = await secondContext.Query(new ContextQuery(new ContextPath("gmail.search")));

        Assert.NotNull(entry);
        Assert.Equal("gmail.search.v1", entry!.SchemaHash);
        Assert.Contains("New Customer", entry.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartExecution_TeamWorkload_runs_researcher_then_closer_and_writes_team_trace()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;
        var executionId = ExecutionId.New();
        var name = executionId.ToString();
        var exec = brain.GetGrainProxy<IExecution>(name);

        await exec.HandleAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                new TeamWorkload(
                    Goal: "Research lead then close in Salesforce",
                    ParticipantNames: ["researcher", "closer"]),
                ExecutionDriverKind.Team,
                [
                    CapabilityId.Parse("gmail.search"),
                    CapabilityId.Parse("websearch.company"),
                    CapabilityId.Parse("salesforce.upsert"),
                ]),
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
        var gmail = await ctx.Query(new ContextQuery(new ContextPath("gmail.search")));
        var websearch = await ctx.Query(new ContextQuery(new ContextPath("websearch.company")));
        var salesforce = await ctx.Query(new ContextQuery(new ContextPath("salesforce.upsert")));
        var teamTrace = await ctx.Query(new ContextQuery(new ContextPath("team.trace")));

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

        var projection = await exec.Read();
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.Equal(ExecutionDriverKind.Team, projection.Driver);
        Assert.IsType<TeamWorkload>(projection.Workload);
    }
}
