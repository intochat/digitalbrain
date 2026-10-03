using DigitalBrain.Platform.Auth;
using DigitalBrain.Platform.Contracts.Auth;

namespace DigitalBrain.Platform.Tests.Unit;

public sealed class TokenHandoffFacts
{
    [Fact]
    public async Task ConcurrentTakesReleaseTokensExactlyOnce()
    {
        var handoff = new TokenHandoff(TimeProvider.System);
        var nonce = handoff.Deposit(new("access", "refresh"));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() => handoff.TryTake(nonce, out _))));
        Assert.Single(results, taken => taken);
    }

    [Fact]
    public void ExpirationReleasesCapacityAndRejectsOldTokens()
    {
        var clock = new Clock();
        var handoff = new TokenHandoff(clock);
        var nonce = handoff.Deposit(new("access", null));
        for (var i = 1; i < 128; i++) { handoff.Deposit(new("access", null)); }
        Assert.Throws<InvalidOperationException>(() => handoff.Deposit(new("overflow", null)));
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False(handoff.TryTake(nonce, out _));
        Assert.True(handoff.TryTake(handoff.Deposit(new("new", null)), out var tokens));
        Assert.Equal("new", tokens.AccessToken);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
