using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class FixtureExclusivity(
    TestingAppHostFixture testing,
    ProductionAppHostFixture production)
{
    [Fact]
    public async Task ASecondGraphWaitsForTheFirstWithinTheSameFixture()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first = await testing.StartAsync(cancellationToken);
        var waiting = testing.StartAsync(cancellationToken);
        Assert.False(waiting.IsCompleted);

        await first.DisposeAsync();
        await using var second = await waiting;
        Assert.NotNull(second);
    }

    [Fact]
    public async Task ASecondGraphWaitsForTheFirstAcrossFixtureTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first = await testing.StartAsync(cancellationToken);
        var waiting = production.StartAsync(cancellationToken);
        Assert.False(waiting.IsCompleted);

        await first.DisposeAsync();
        await using var second = await waiting;
        Assert.NotNull(second);
    }
}
