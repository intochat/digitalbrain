using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class ClockContracts(TestingFixture fixture)
{
    private static readonly DateTimeOffset FixedEpoch =
        new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "TestClock starts at the fixed epoch and AdvanceAsync moves UtcNow")]
    public async Task ClockStartsAtFixedEpochAndAdvances()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        Assert.Equal(FixedEpoch, test.Clock.UtcNow);

        await test.Clock.AdvanceAsync(TimeSpan.FromHours(3), cancellationToken);

        Assert.Equal(FixedEpoch + TimeSpan.FromHours(3), test.Clock.UtcNow);
    }

    [Fact(DisplayName = "A later method lease resets the shared clock to the fixed epoch")]
    public async Task NextMethodLeaseResetsClockToFixedEpoch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var first = await fixture.CreateBrainAsync(cancellationToken))
        {
            Assert.Equal(FixedEpoch, first.Clock.UtcNow);
            await first.Clock.AdvanceAsync(TimeSpan.FromDays(9), cancellationToken);
            Assert.Equal(FixedEpoch + TimeSpan.FromDays(9), first.Clock.UtcNow);
        }

        await using var second = await fixture.CreateBrainAsync(cancellationToken);
        Assert.Equal(FixedEpoch, second.Clock.UtcNow);
    }
}
