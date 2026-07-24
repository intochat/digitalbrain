using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TestingFrameworkContracts
{
    [Fact(DisplayName = "Simulations.OpenAsync returns a Scenario with a non-default unique Owner")]
    public async Task OpenAsyncReturnsScenarioWithNonDefaultOwner()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(default, scenario.Owner);
        Assert.False(string.IsNullOrWhiteSpace(scenario.Owner.Value));
    }

    [Fact(DisplayName = "sequential Simulations.OpenAsync calls receive different Owners")]
    public async Task SequentialOpensReceiveDifferentOwners()
    {
        await using var first = await Simulations.OpenAsync(TestContext.Current.CancellationToken);
        await using var second = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Owner, second.Owner);
    }

    [Fact(DisplayName = "disposing a Scenario does not stop the assembly cluster for the next OpenAsync")]
    public async Task DisposeDoesNotStopAssemblyCluster()
    {
        var first = await Simulations.OpenAsync(TestContext.Current.CancellationToken);
        await first.DisposeAsync();

        await using var second = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(second.Grains);
        Assert.NotEqual(default, second.Owner);
    }

    [Fact(DisplayName = "Scenario.Grains is a live grain factory after OpenAsync without calling SimulationCluster.StartAsync")]
    public async Task OpenAsyncStartsClusterAndExposesLiveGrains()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(scenario.Grains);
        Assert.NotNull(scenario.Clock);
        Assert.IsType<ScenarioClock>(scenario.Clock);
        Assert.NotSame(TimeProvider.System, scenario.Clock);
    }

    [Fact(DisplayName = "Scenario.Clock starts at a recorded instant and stays put until AdvanceClock")]
    public async Task ClockStartsAtRecordedInstant()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        var first = scenario.Clock.GetUtcNow();
        var second = scenario.Clock.GetUtcNow();

        Assert.Equal(first, second);
        Assert.NotEqual(default, first);
    }

    [Fact(DisplayName = "Scenario.AdvanceClock moves GetUtcNow by the requested delta without wall-clock sleep")]
    public async Task AdvanceClockMovesGetUtcNow()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        var before = scenario.Clock.GetUtcNow();
        scenario.AdvanceClock(TimeSpan.FromMinutes(5));

        Assert.Equal(before + TimeSpan.FromMinutes(5), scenario.Clock.GetUtcNow());
    }

    [Fact(DisplayName = "host ScenarioClock is registered in silo DI so neuron journal stamps follow AdvanceClock")]
    public async Task AdvanceClockStampsNeuronJournalDeliveries()
    {
        await using var scenario = await Simulations.OpenAsync(TestContext.Current.CancellationToken);

        var before = scenario.Clock.GetUtcNow();
        scenario.AdvanceClock(TimeSpan.FromHours(3));
        var expected = before + TimeSpan.FromHours(3);

        var sessionId = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "session");
        var peerId = new NeuronId(ISessionNeuron.GrainTypeName, scenario.Owner, "peer");
        var session = scenario.Grains.GetGrain<ISessionNeuron>(sessionId.ToGrainId());

        await session.FireAsync(peerId, new CapabilityRequested("IProbe", "Ping", peerId));

        var journal = await session.ReadNeuronJournalAsync(sessionId, JournalKind.Outgoing, afterSequence: 0);
        var stamped = Assert.Single(journal.Delta);

        Assert.Equal(expected, stamped.Timestamp);
    }
}
