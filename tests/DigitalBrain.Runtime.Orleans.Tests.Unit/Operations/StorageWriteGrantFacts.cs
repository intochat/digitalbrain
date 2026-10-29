using DigitalBrain.Runtime.Orleans.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Operations;

public sealed class StorageWriteGrantFacts
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientCleanupFailureDoesNotRetryOrChangeTheWriteOutcome(bool writeFails)
    {
        var writes = 0;
        var failure = new InvalidOperationException("write failed");
        var sending = StorageWriteAuthorizer.SendAndRevoke(() =>
        {
            writes++;
            return writeFails ? Task.FromException<string>(failure) : Task.FromResult("written");
        }, () => Task.FromException(new TimeoutException("cleanup timed out")), NullLogger.Instance);
        if (writeFails) { Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => sending)); }
        else { Assert.Equal("written", await sending); }
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task GrantsAreExactSingleUseAndExpire()
    {
        var clock = new Clock();
        var grants = new StorageWriteGrantGrain(clock);
        var token = await grants.Issue("owner/connections/disk", "person", "plan");
        Assert.Equal(64, token.Length);
        Assert.True(await grants.Consume(token, "owner/connections/disk", "person", "plan"));
        Assert.False(await grants.Consume(token, "owner/connections/disk", "person", "plan"));
        foreach (var changed in new[] { "connection", "person", "plan" })
        {
            token = await grants.Issue("owner/connections/disk", "person", "plan");
            Assert.False(await grants.Consume(token, changed == "connection" ? "other/connections/disk" : "owner/connections/disk",
                changed == "person" ? "agent" : "person", changed == "plan" ? "different" : "plan"));
            Assert.False(await grants.Consume(token, "owner/connections/disk", "person", "plan"));
        }
        token = await grants.Issue("owner/connections/disk", "person", "plan");
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.False(await grants.Consume(token, "owner/connections/disk", "person", "plan"));
        Assert.False(await new StorageWriteGrantGrain(clock).Consume(token, "owner/connections/disk", "person", "plan"));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
