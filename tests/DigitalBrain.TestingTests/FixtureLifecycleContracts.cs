using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class FixtureLifecycleContracts(TestingFixture fixture)
{
    [Fact(DisplayName = "A method lease does not stop the assembly-scoped fixture cluster")]
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

    [Fact(DisplayName = "A second method lease waits until the first is disposed")]
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
