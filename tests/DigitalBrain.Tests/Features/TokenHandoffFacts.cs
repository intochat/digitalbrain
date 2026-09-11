using DigitalBrain.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TokenHandoffFacts
{
    [Fact]
    public void BundlesCanBePeekedUntilConsumedButNeverAfterExpiry()
    {
        var clock = new FakeTimeProvider();
        var handoff = new TokenHandoff(clock);
        var tokens = new OAuthTokens("access", "refresh");
        var nonce = handoff.Deposit(tokens);
        Assert.True(handoff.TryPeek(nonce, out var redeemed));
        Assert.Equal(tokens, redeemed);
        Assert.True(handoff.TryPeek(nonce, out var retried));
        Assert.Equal(tokens, retried);
        handoff.Consume(nonce);
        Assert.False(handoff.TryPeek(nonce, out _));

        var expired = handoff.Deposit(tokens);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(handoff.TryPeek(expired, out _));
    }
}
