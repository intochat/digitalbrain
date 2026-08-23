using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Core;
using DigitalBrain.Execution;
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
}
