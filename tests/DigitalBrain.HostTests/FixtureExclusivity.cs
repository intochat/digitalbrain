using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class FixtureExclusivity(
    TestingAppHostFixture testing,
    ProductionAppHostFixture production)
{
    [Fact]
    public async Task ASecondGraphWaitsForTheFirstAcrossFixtureTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first = await testing.StartAsync(cancellationToken);
        var waiting = production.StartAsync(cancellationToken);
        RunningAppHost? second = null;

        try
        {
            Assert.False(waiting.IsCompleted);
            await first.DisposeAsync();

            second = await waiting;
            Assert.NotNull(second);
        }
        finally
        {
            await first.DisposeAsync();
            second ??= await waiting;
            await second.DisposeAsync();
        }
    }
}
