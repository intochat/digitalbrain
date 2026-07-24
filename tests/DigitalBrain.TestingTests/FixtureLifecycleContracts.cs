using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class FixtureLifecycleContracts(TestingFixture fixture)
{
    [Fact]
    public async Task AMethodLeaseDoesNotStopTheAssemblyCluster()
    {
        await using (var first =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken))
        {
            Assert.NotNull(first);
        }

        await using var second =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task ASecondMethodLeaseWaitsUntilTheFirstIsDisposed()
    {
        await using var first =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        var waiting = fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        Assert.False(waiting.IsCompleted);

        await first.DisposeAsync();
        await using var second = await waiting;
        Assert.NotNull(second);
    }
}
