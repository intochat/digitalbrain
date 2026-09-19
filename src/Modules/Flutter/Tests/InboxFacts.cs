using DigitalBrain.Flutter;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class InboxFacts
{
    [Fact]
    public async Task InboxHttpReturnsAppearedLines()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new()
        {
            Modules = [new FlutterModule()],
            UseHttp = true,
        }, ct);
        await brain.Get<IInbox>("ui").Appear("hello inbox");
        var json = await brain.Http().GetStringAsync(FlutterModule.InboxPath, ct);
        Assert.Contains("hello inbox", json, StringComparison.Ordinal);
    }
}
