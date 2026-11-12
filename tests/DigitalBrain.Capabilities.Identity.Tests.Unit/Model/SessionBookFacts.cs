namespace DigitalBrain.Capabilities.Identity.Tests.Unit.Model;

using DigitalBrain.Capabilities.Identity.Model;
using Xunit;

public sealed class SessionBookFacts
{
    [Fact]
    public void StartStoresHashedTokenAndNeverRawToken()
    {
        var book = new SessionBook();
        var rawToken = "raw-super-secret-token-12345";
        var tokenHash = SessionBook.HashToken(rawToken);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(1);

        var (next, session, facts) = book.Start(tokenHash, "b-1", "p-1", expires, now);
        Assert.Single(facts);
        var signal = Assert.IsType<SessionStarted>(facts[0]);
        Assert.Equal("p-1", signal.PrincipalId);
        Assert.Equal(tokenHash, signal.TokenHash);

        // Verify raw token is NOT in state
        Assert.DoesNotContain(next.Sessions, s => s.TokenHash.Contains("raw-super-secret", StringComparison.Ordinal));
        Assert.Equal(tokenHash, session.TokenHash);

        // Validation by raw token hash succeeds
        var validated = next.Find(tokenHash, now.AddMinutes(10));
        Assert.NotNull(validated);
        Assert.Equal("p-1", validated.PrincipalId);
    }

    [Fact]
    public void StartAutoPrunesExpiredSessions()
    {
        var book = new SessionBook();
        var now = DateTimeOffset.UtcNow;

        var (bookWithExpired, _, _) = book.Start("hash-expired", "b-1", "p-1", now.AddMinutes(-5), now.AddMinutes(-10));
        Assert.Single(bookWithExpired.Sessions);

        // Starting a new session prunes expired ones
        var (cleanBook, _, _) = bookWithExpired.Start("hash-new", "b-1", "p-2", now.AddHours(1), now);
        Assert.Single(cleanBook.Sessions);
        Assert.Null(cleanBook.Find("hash-expired", now));
        Assert.NotNull(cleanBook.Find("hash-new", now));
    }

    [Fact]
    public void EndRemovesSession()
    {
        var book = new SessionBook();
        var now = DateTimeOffset.UtcNow;
        var (started, _, _) = book.Start("hash-1", "b-1", "p-1", now.AddHours(1), now);

        var (ended, _) = started.End("hash-1");
        Assert.Empty(ended.Sessions);
        Assert.Null(ended.Find("hash-1", now));
    }
}
