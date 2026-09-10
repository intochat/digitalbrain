using DigitalBrain.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TokenHandoffFacts
{
    [Fact]
    public void BundlesCanBeRedeemedExactlyOnceAndNeverAfterExpiry()
    {
        var clock = new FakeTimeProvider();
        var handoff = new TokenHandoff(clock);
        var tokens = new OAuthTokens("access", "refresh");
        var nonce = handoff.Deposit(tokens);
        Assert.True(handoff.TryRedeem(nonce, out var redeemed));
        Assert.Equal(tokens, redeemed);
        Assert.False(handoff.TryRedeem(nonce, out _));

        var expired = handoff.Deposit(tokens);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(handoff.TryRedeem(expired, out _));
    }
}
