using DigitalBrain.Abstractions;
using DigitalBrain.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class ExecutionSimulationFixture : IAsyncLifetime
{
    private BrainSimulation? _sim;

    public BrainSimulation Sim => _sim ?? throw new InvalidOperationException("Simulation has not started.");

    public async ValueTask InitializeAsync()
        => _sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(ExecutionModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
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
    public async Task StartExecution_completes_a_chat_turn_workload()
    {
        var brain = fixture.Sim.Brain;
        var executionId = ExecutionId.New();

        var execution = await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            executionId,
            new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "hi"),
            cancellationToken: TestContext.Current.CancellationToken);

        var projection = await execution.RequestAsync(
            new ReadExecution(),
            TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.Equal(executionId, projection.ExecutionId);
        Assert.IsType<ChatTurnWorkload>(projection.Workload);
    }

    [Fact]
    public async Task StartExecution_copies_related_execution_context_slots()
    {
        var brain = fixture.Sim.Brain;
        var cancellationToken = TestContext.Current.CancellationToken;

        var firstId = ExecutionId.New();
        var firstTurn = Guid.NewGuid();
        await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            firstId,
            new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), firstTurn, "first"),
            cancellationToken: cancellationToken);

        var secondId = ExecutionId.New();
        await ExecutionTestDriver.StartAndCompleteAsync(
            brain,
            secondId,
            new ChatTurnWorkload(new NeuronId("chat", brain.Owner, "main"), Guid.NewGuid(), "follow-up"),
            relatedExecutions: [firstId],
            cancellationToken: cancellationToken);

        var secondContext = brain.GetEntity<IExecutionContext>(secondId.ToString());
        var entry = await secondContext.Query(new ContextQuery(new ContextPath($"chat.turn.{firstTurn:N}")));

        Assert.NotNull(entry);
        Assert.Equal("chat.turn.v1", entry!.SchemaHash);
        Assert.Contains("first", entry.PayloadJson, StringComparison.Ordinal);
    }
}
