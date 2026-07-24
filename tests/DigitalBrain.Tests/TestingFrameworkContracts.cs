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
    }
}
