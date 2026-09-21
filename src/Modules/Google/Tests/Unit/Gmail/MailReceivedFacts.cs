using DigitalBrain.Testing.Unit;
using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MailReceivedFacts
{
    [Fact]
    public async Task WatchPushPublishesMailReceived()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<GoogleModule>()
            .StartAsync(ct);
        var gmail = brain.Get<IGmail>("me");
        await using var mail = await brain.Observe<MailReceived>(gmail, ct);
        await gmail.AcceptWatchPush(new("123", "user@gmail.com"));
        var received = await mail.NextAsync(ct: ct);
        Assert.Equal("user@gmail.com", received.EmailAddress);
        Assert.Equal("123", received.HistoryId);
    }
}
