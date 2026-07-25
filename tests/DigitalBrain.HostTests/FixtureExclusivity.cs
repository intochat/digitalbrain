using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class FixtureExclusivity(
    TestingAppHostFixture testing,
    QuickstartAppHostFixture quickstart)
{
    [Fact(DisplayName =
        "a second graph waits for the first within the same AppHost fixture")]
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

    [Fact(DisplayName =
        "a second graph waits for the first across silo-only AppHost fixture types")]
    public async Task ASecondGraphWaitsForTheFirstAcrossFixtureTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first = await testing.StartAsync(cancellationToken);
        var waiting = quickstart.StartAsync(cancellationToken);
        Assert.False(waiting.IsCompleted);

        await first.DisposeAsync();
        await using var second = await waiting;
        Assert.NotNull(second);
    }
}
