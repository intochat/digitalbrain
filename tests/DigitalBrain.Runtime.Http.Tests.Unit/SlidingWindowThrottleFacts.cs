using DigitalBrain.Runtime.Http.Surface;
using DigitalBrain.Testing;

namespace DigitalBrain.Runtime.Http.Tests.Unit;

public sealed class SlidingWindowThrottleFacts
{
    [Fact]
    public async Task ThrottleAllowsLimitThenDenies()
    {
        var clock = new ManualTime();
        var window = TimeSpan.FromMinutes(1);
        var throttle = new SlidingWindowThrottle(2, window, clock);
        var ct = TestContext.Current.CancellationToken;

        var first = await throttle.HitAsync("192.0.2.0/24", ct);
        var second = await throttle.HitAsync("192.0.2.0/24", ct);
        var third = await throttle.HitAsync("192.0.2.0/24", ct);

        Assert.True(first.Allowed);
        Assert.Null(first.RetryAt);
        Assert.True(second.Allowed);
        Assert.Null(second.RetryAt);
        Assert.False(third.Allowed);
        Assert.Equal(clock.GetUtcNow() + window, third.RetryAt);
    }
}
