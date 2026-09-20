using System.Net.Http.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Inbox;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class InboxHttpFacts
{
    [Fact]
    public async Task GetReturnsAppearedLines()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<IInbox>(FlutterModule.InboxGrain).Appear("hello inbox");
        var lines = await brain.HttpClient.GetFromJsonAsync<List<string>>(FlutterModule.InboxPath, ct);
        Assert.Contains("hello inbox", lines!, StringComparer.Ordinal);
    }
}
