using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Inbox;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class InboxFacts
{
    [Fact]
    public async Task InboxReadReturnsAppearedLines()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var inbox = brain.Get<IInbox>(FlutterModule.InboxGrain);
        await inbox.Appear("hello inbox");
        Assert.Contains("hello inbox", await inbox.Read(), StringComparer.Ordinal);
    }
}
