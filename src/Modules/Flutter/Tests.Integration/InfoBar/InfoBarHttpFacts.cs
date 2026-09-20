using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.InfoBar;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class InfoBarHttpFacts
{
    [Fact]
    public async Task GetMatchesShow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        await brain.Get<IInfoBar>("warn").Show("warning", "Heads up", "disk");
        var state = await brain.HttpClient.GetFromJsonAsync<InfoBarState>("/ui/infobars/warn", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.True(state!.Visible);
    }
}
