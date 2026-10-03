using DigitalBrain.Sdk;

namespace DigitalBrain.Sdk.Tests.Unit;

public sealed class BrowserLoginsFacts
{
    [Fact]
    public async Task ALoginMustBeginBeforeItCanBeClaimedAndOnlyOneClaimWins()
    {
        var logins = new Logins(new Clock());
        var id = logins.Require("scope").Query.Split('=')[1];
        Assert.False(logins.TryClaim(id));
        Assert.True(logins.TryBegin(id, out var scope));
        Assert.Equal("scope", scope);
        Assert.False(logins.TryBegin(id, out _));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => logins.TryClaim(id))));
        Assert.Single(results, claimed => claimed);
    }

    [Fact]
    public void ExpiredRequestsCannotBeginOrClaimAndDoNotOccupyCapacity()
    {
        var clock = new Clock();
        var logins = new Logins(clock);
        var id = logins.Require().Query.Split('=')[1];
        Assert.True(logins.TryBegin(id, out _));
        Assert.Throws<InvalidOperationException>(() => logins.Require());
        clock.Now += TimeSpan.FromMinutes(10);
        Assert.False(logins.TryClaim(id));
        Assert.False(logins.TryBegin(id, out _));
        Assert.NotEqual(id, logins.Require().Query.Split('=')[1]);
    }

    private sealed class Logins(TimeProvider clock) : BrowserLogins(new("test", "Test", "test", "/login", "/callback", "Sign in") { Capacity = 1 }, clock)
    {
        protected override Uri? PublicOrigin => new("https://example.test");
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
